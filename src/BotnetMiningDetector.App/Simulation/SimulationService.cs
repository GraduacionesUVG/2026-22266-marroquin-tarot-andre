using System.Diagnostics;
using System.IO;

using BotnetMiningDetector.App.Agent;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Simulation;

// servicio de escenarios controlados
public sealed class SimulationService
{
    private const int SimulationDurationSeconds = 150;
    private readonly SqliteRepository _repository;
    private readonly AgentSettings _settings;
    private readonly object _stateLock = new();
    private Process? _activeProcess;
    private ValidationScenario? _activeScenario;
    private ScenarioTelemetryAccumulator? _activeTelemetry;

    // crea el servicio de simulacion
    public SimulationService(SqliteRepository repository, AgentSettings settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public ValidationScenario? ActiveScenario
    {
        get
        {
            lock (_stateLock)
            {
                return _activeScenario;
            }
        }
    }

    public bool HasActiveSimulation
    {
        get
        {
            lock (_stateLock)
            {
                return _activeProcess is { HasExited: false };
            }
        }
    }

    // inicia un escenario seguro
    public async Task<ValidationScenario> StartScenarioAsync(string scenarioType, CancellationToken cancellationToken = default)
    {
        await StopActiveScenarioAsync("Se inició un nuevo escenario.", cancellationToken);

        if (scenarioType.Equals("UsoNormal", StringComparison.OrdinalIgnoreCase))
        {
            var normalScenario = new ValidationScenario
            {
                Name = "Uso normal",
                ScenarioType = "UsoNormal",
                ScenarioCategory = "Legítimo",
                Description = "Ventana de observación sin carga artificial para medir falsos positivos.",
                ProcessName = "No aplica",
                StartedAt = DateTime.Now,
                ExpectedResult = "No debería generar alertas durante la ventana de observación.",
                ActualResult = "Observación normal en ejecución.",
                ExpectedClassification = "Sin alerta",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                AlertGenerated = false,
                Notes = "No se ejecuta simulador. Detenga el escenario para cerrar la ventana normal."
            };
            await _repository.InsertValidationScenarioAsync(normalScenario, cancellationToken);
            lock (_stateLock)
            {
                _activeScenario = normalScenario;
                _activeTelemetry = new ScenarioTelemetryAccumulator(useSystemMetrics: true);
            }

            ScheduleAutomaticStop(normalScenario);

            return normalScenario;
        }

        var simulatorPath = ResolveSimulatorPath();
        var scenario = CreateScenarioRecord(scenarioType);
        await _repository.InsertValidationScenarioAsync(scenario, cancellationToken);

        if (simulatorPath is null)
        {
            scenario.FinishedAt = DateTime.Now;
            scenario.DurationSeconds = Math.Round((scenario.FinishedAt.Value - scenario.StartedAt).TotalSeconds, 2);
            scenario.ActualResult = "No se encontró el ejecutable del simulador. Compile la solución completa antes de ejecutar la validación.";
            scenario.ActualClassification = "No evaluado";
            scenario.ConfusionResult = "Pendiente";
            scenario.Notes = "Revise SimulatorExecutablePath en appsettings.json si usa una ruta personalizada.";
            await _repository.UpdateValidationScenarioAsync(scenario, cancellationToken);
            return scenario;
        }

        try
        {
            if (scenarioType.Equals("RutaSospechosaSimulada", StringComparison.OrdinalIgnoreCase))
            {
                simulatorPath = PrepareSuspiciousPathSimulator(simulatorPath);
            }

            var mode = ScenarioMode(scenarioType);
            var durationSeconds = ScenarioDurationSeconds(scenarioType);

            var startInfo = new ProcessStartInfo
            {
                FileName = simulatorPath,
                Arguments = $"--mode={mode} --duration={durationSeconds} --port={_settings.LocalhostBotnetPort}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                await MarkTechnicalFailureAsync(scenario, "No se pudo iniciar el proceso simulador.", cancellationToken);
                return scenario;
            }

            lock (_stateLock)
            {
                _activeProcess = process;
                _activeScenario = scenario;
                _activeTelemetry = new ScenarioTelemetryAccumulator(useSystemMetrics: false);
            }

            scenario.ProcessId = process.Id;
            scenario.ActualResult = $"Simulador iniciado con PID {process.Id}.";
            scenario.ProcessName = SafeProcessName(process, scenario.ProcessName);
            scenario.ActualClassification = "En observación";
            await _repository.UpdateValidationScenarioAsync(scenario, cancellationToken);

            ScheduleAutomaticStop(scenario);
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            if (process.HasExited)
            {
                await FinalizeExitedScenarioAsync(process, scenario, durationSeconds, cancellationToken);
                return scenario;
            }

            _ = MonitorScenarioProcessAsync(process, scenario, durationSeconds);
            return scenario;
        }
        catch (OperationCanceledException)
        {
            await StopActiveScenarioAsync("Inicio de simulación cancelado.", CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await MarkTechnicalFailureAsync(
                scenario,
                $"No se pudo preparar o iniciar el simulador: {ex.Message}",
                cancellationToken);
            return scenario;
        }
    }

    // detiene el escenario activo
    public async Task StopActiveScenarioAsync(string note = "Detenido desde la interfaz.", CancellationToken cancellationToken = default)
    {
        Process? process;
        ValidationScenario? scenario;
        ScenarioTelemetryAccumulator? telemetry;
        lock (_stateLock)
        {
            process = _activeProcess;
            scenario = _activeScenario;
            telemetry = _activeTelemetry;
            _activeProcess = null;
            _activeScenario = null;
            _activeTelemetry = null;
        }

        if (process is { HasExited: false })
        {
            try
            {
                // solo detiene el simulador controlado por este modulo
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                // la simulacion pudo terminar durante la detencion
            }
        }

        if (scenario is not null && scenario.FinishedAt is null)
        {
            telemetry?.ApplyTo(scenario);
            scenario.FinishedAt = DateTime.Now;
            scenario.DurationSeconds = Math.Round((scenario.FinishedAt.Value - scenario.StartedAt).TotalSeconds, 2);
            if (!scenario.AlertGenerated)
            {
                var hadContextualWarning = scenario.ActualClassification.Equals("Advertencia contextual", StringComparison.OrdinalIgnoreCase);
                scenario.ActualResult = hadContextualWarning
                    ? "Ventana legítima finalizada con advertencia de baja severidad, sin alerta definitiva."
                    : IsLegitimate(scenario)
                        ? "Ventana legítima finalizada sin alertas."
                        : "Finalizado sin alerta registrada durante la ventana de observación.";
                scenario.ActualClassification = hadContextualWarning ? "Sin alerta definitiva" : "Sin alerta";
                scenario.ConfusionResult = IsLegitimate(scenario) ? "TN" : "FN";
            }

            scenario.Notes = string.IsNullOrWhiteSpace(scenario.Notes)
                ? note
                : $"{scenario.Notes} {note}";
            await _repository.UpdateValidationScenarioAsync(scenario, cancellationToken);
        }

        process?.Dispose();
    }

    // registra telemetria del escenario
    public void ObserveSnapshot(TelemetrySnapshot snapshot)
    {
        lock (_stateLock)
        {
            _activeTelemetry?.Observe(snapshot, _activeProcess?.Id);
        }
    }

    // relaciona alertas con el escenario
    public async Task NotifyAlertsAsync(IEnumerable<Alert> alerts, CancellationToken cancellationToken = default)
    {
        var alertList = alerts.ToList();
        ValidationScenario? activeScenario;
        Process? activeProcess;
        lock (_stateLock)
        {
            activeScenario = _activeScenario;
            activeProcess = _activeProcess;
        }

        if (activeScenario is null || activeScenario.AlertGenerated || alertList.Count == 0)
        {
            return;
        }

        var activeProcessId = activeProcess?.Id;
        var isNormalObservation = activeScenario.ScenarioType.Equals("UsoNormal", StringComparison.OrdinalIgnoreCase);
        var isLegitimateLoad = activeScenario.ScenarioType.Equals("CargaLegitimaTemporal", StringComparison.OrdinalIgnoreCase);

        if (isLegitimateLoad && activeProcessId is not null)
        {
            var contextualWarning = alertList.FirstOrDefault(alert =>
                alert.ProcessId == activeProcessId &&
                alert.Severity.Equals("Baja", StringComparison.OrdinalIgnoreCase));

            if (contextualWarning is not null)
            {
                lock (_stateLock)
                {
                    _activeTelemetry?.ApplyTo(activeScenario);
                }

                activeScenario.DurationSeconds = Math.Round((DateTime.Now - activeScenario.StartedAt).TotalSeconds, 2);
                activeScenario.ActualResult = $"Advertencia contextual: {contextualWarning.AlertType} (Baja).";
                activeScenario.ActualClassification = "Advertencia contextual";
                activeScenario.ProcessId = contextualWarning.ProcessId;
                activeScenario.ProcessName = contextualWarning.ProcessName;
                activeScenario.TriggeredRuleName = contextualWarning.RuleName;
                activeScenario.ObservedActivity = contextualWarning.MetricObserved;
                activeScenario.Notes = $"Advertencia baja del proceso controlado: {contextualWarning.ProcessName}. Regla: {contextualWarning.RuleName}. No se clasifica como alerta definitiva.";
                await _repository.UpdateValidationScenarioAsync(activeScenario, cancellationToken);
            }
        }

        var matchingAlert = isNormalObservation
            ? alertList.FirstOrDefault()
            : alertList.FirstOrDefault(alert =>
                activeProcessId is not null &&
                alert.ProcessId == activeProcessId &&
                (!isLegitimateLoad || !alert.Severity.Equals("Baja", StringComparison.OrdinalIgnoreCase)) &&
                MatchesExpectedAlert(activeScenario, alert));

        if (matchingAlert is null)
        {
            return;
        }

        lock (_stateLock)
        {
            _activeTelemetry?.ApplyTo(activeScenario);
        }

        activeScenario.AlertGenerated = true;
        activeScenario.DetectionTimeSeconds = Math.Round((matchingAlert.Timestamp - activeScenario.StartedAt).TotalSeconds, 2);
        activeScenario.DurationSeconds = Math.Round((DateTime.Now - activeScenario.StartedAt).TotalSeconds, 2);
        activeScenario.ActualResult = $"Alerta generada: {matchingAlert.AlertType} ({matchingAlert.Severity}).";
        activeScenario.ActualClassification = "Alerta generada";
        activeScenario.ConfusionResult = IsLegitimate(activeScenario) ? "FP" : "TP";
        activeScenario.ProcessId = matchingAlert.ProcessId;
        activeScenario.ProcessName = matchingAlert.ProcessName;
        activeScenario.TriggeredRuleName = matchingAlert.RuleName;
        activeScenario.ObservedActivity = matchingAlert.MetricObserved;
        activeScenario.Notes = $"Proceso observado: {matchingAlert.ProcessName}. Regla: {matchingAlert.RuleName}.";
        await _repository.UpdateValidationScenarioAsync(activeScenario, cancellationToken);
    }

    // crea el registro esperado del escenario
    private static ValidationScenario CreateScenarioRecord(string scenarioType)
    {
        return scenarioType switch
        {
            "CargaLegitimaTemporal" => new ValidationScenario
            {
                Name = "Carga legítima temporal",
                ScenarioType = "CargaLegitimaTemporal",
                ScenarioCategory = "Legítimo",
                Description = "Carga controlada de corta duración para observar si el agente evita alertas por actividad temporal.",
                ProcessName = "BotnetMiningDetector.Simulator",
                StartedAt = DateTime.Now,
                ExpectedResult = "No debería generar alerta definitiva; si aparece una advertencia, debe ser de baja criticidad contextual.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Sin alerta",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "Carga temporal segura, sin minería real."
            },
            "CriptomineriaSimulada" => new ValidationScenario
            {
                Name = "Criptominería simulada",
                ScenarioType = "CriptomineriaSimulada",
                ScenarioCategory = "Sospechoso",
                Description = "Proceso controlado con CPU sostenida para simular abuso de recursos compatible con criptominería.",
                ProcessName = "BotnetMiningDetector.Simulator",
                StartedAt = DateTime.Now,
                ExpectedResult = "Debe generar alerta por CPU elevada o proceso nuevo con alta carga.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Alerta esperada",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "Carga CPU controlada, sin minería real."
            },
            "BotnetSimulada" => new ValidationScenario
            {
                Name = "Botnet simulada",
                ScenarioType = "BotnetSimulada",
                ScenarioCategory = "Sospechoso",
                Description = "Actividad TCP periódica solo contra localhost para simular comunicación automatizada controlada.",
                ProcessName = "BotnetMiningDetector.Simulator",
                StartedAt = DateTime.Now,
                ExpectedResult = "Debe generar alerta por actividad periódica local.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Alerta esperada",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "Actividad TCP solo contra localhost."
            },
            "ProcesoPersistenteSimulado" => new ValidationScenario
            {
                Name = "Proceso persistente simulado",
                ScenarioType = "ProcesoPersistenteSimulado",
                ScenarioCategory = "Sospechoso",
                Description = "Proceso controlado que permanece activo en segundo plano para evaluar la regla de persistencia.",
                ProcessName = "BotnetMiningDetector.Simulator",
                StartedAt = DateTime.Now,
                ExpectedResult = "Debe generar alerta si permanece activo el tiempo configurado y se combina con señales observables.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Alerta esperada",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "Proceso persistente seguro iniciado por el prototipo."
            },
            "RutaSospechosaSimulada" => new ValidationScenario
            {
                Name = "Nombre o ruta sospechosa",
                ScenarioType = "RutaSospechosaSimulada",
                ScenarioCategory = "Sospechoso",
                Description = "Copia controlada del simulador con nombre similar a un servicio de Windows en una ruta no estándar.",
                ProcessName = "svchost",
                StartedAt = DateTime.Now,
                ExpectedResult = "Debe generar alerta contextual por posible camuflaje de proceso.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Alerta esperada",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "No reemplaza archivos del sistema; usa una copia local controlada para laboratorio."
            },
            _ => new ValidationScenario
            {
                Name = "Escenario combinado",
                ScenarioType = "Combinado",
                ScenarioCategory = "Sospechoso",
                Description = "Combina CPU alta, proceso nuevo, ejecución prolongada y actividad periódica local.",
                ProcessName = "BotnetMiningDetector.Simulator",
                StartedAt = DateTime.Now,
                ExpectedResult = "Debe generar alerta por correlación de proceso nuevo, CPU y actividad periódica.",
                ActualResult = "En ejecución.",
                ExpectedClassification = "Alerta esperada",
                ActualClassification = "En observación",
                ConfusionResult = "Pendiente",
                Notes = "Combina carga CPU controlada y conexiones localhost."
            }
        };
    }

    // convierte el tipo al modo del simulador
    private static string ScenarioMode(string scenarioType)
    {
        return scenarioType switch
        {
            "CargaLegitimaTemporal" => "legitimate",
            "CriptomineriaSimulada" => "miner",
            "BotnetSimulada" => "botnet",
            "ProcesoPersistenteSimulado" => "persistent",
            "RutaSospechosaSimulada" => "suspicious-path",
            _ => "combined"
        };
    }

    // obtiene la duracion del escenario
    private static int ScenarioDurationSeconds(string scenarioType)
    {
        return SimulationDurationSeconds;
    }

    // detiene automaticamente el escenario al completar su duracion
    private void ScheduleAutomaticStop(ValidationScenario scenario)
    {
        _ = StopScenarioAfterDurationAsync(scenario);
    }

    private async Task StopScenarioAfterDurationAsync(ValidationScenario scenario)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(SimulationDurationSeconds));

            lock (_stateLock)
            {
                if (!ReferenceEquals(_activeScenario, scenario))
                {
                    return;
                }
            }

            await StopActiveScenarioAsync(
                "Duración automática completada: 2 minutos y 30 segundos.",
                CancellationToken.None);
        }
        catch
        {
            // el escenario puede haber terminado antes del temporizador
        }
    }

    // identifica escenarios legitimos
    private static bool IsLegitimate(ValidationScenario scenario)
    {
        return scenario.ScenarioCategory.Equals("Legítimo", StringComparison.OrdinalIgnoreCase) ||
            scenario.ScenarioType.Equals("UsoNormal", StringComparison.OrdinalIgnoreCase) ||
            scenario.ScenarioType.Equals("CargaLegitimaTemporal", StringComparison.OrdinalIgnoreCase);
    }

    // valida si una alerta coincide con el escenario
    private static bool MatchesExpectedAlert(ValidationScenario scenario, Alert alert)
    {
        return scenario.ScenarioType switch
        {
            "CriptomineriaSimulada" => alert.RuleName is
                "CpuProcesoSostenida" or
                "CpuAbsolutaLaboratorio" or
                "ProcesoNuevoAltaCarga" or
                "CorrelacionSenales",
            "BotnetSimulada" => alert.RuleName.Equals("ActividadPeriodicaSimulada", StringComparison.OrdinalIgnoreCase) ||
                CorrelationContainsSignal(alert, "Actividad periódica"),
            "ProcesoPersistenteSimulado" => alert.RuleName.Equals("ProcesoPersistenteNoHabitual", StringComparison.OrdinalIgnoreCase) ||
                CorrelationContainsSignal(alert, "Persistencia"),
            "RutaSospechosaSimulada" => alert.RuleName.Equals("RutaNoEstandar", StringComparison.OrdinalIgnoreCase) ||
                CorrelationContainsSignal(alert, "Ruta sospechosa"),
            "Combinado" => alert.RuleName.Equals("CorrelacionSenales", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    // busca una senal dentro de una correlacion
    private static bool CorrelationContainsSignal(Alert alert, string signal)
    {
        return alert.RuleName.Equals("CorrelacionSenales", StringComparison.OrdinalIgnoreCase) &&
            alert.EvidenceDetails.Contains(signal, StringComparison.OrdinalIgnoreCase);
    }

    // obtiene el nombre del proceso de forma segura
    private static string SafeProcessName(Process process, string fallback)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return fallback;
        }
    }

    // prepara una copia para la ruta sospechosa
    private static string PrepareSuspiciousPathSimulator(string simulatorPath)
    {
        var labDirectory = Path.Combine(AppContext.BaseDirectory, "LabSimulations");
        Directory.CreateDirectory(labDirectory);

        var simulatorDirectory = Path.GetDirectoryName(simulatorPath)
            ?? throw new InvalidOperationException("No se pudo determinar la carpeta del simulador.");
        var simulatorBaseName = Path.GetFileNameWithoutExtension(simulatorPath);
        foreach (var companionFile in Directory.EnumerateFiles(simulatorDirectory, $"{simulatorBaseName}.*"))
        {
            var destination = Path.Combine(labDirectory, Path.GetFileName(companionFile));
            File.Copy(companionFile, destination, overwrite: true);
        }

        var suspiciousPath = Path.Combine(labDirectory, "svchost.exe");
        File.Copy(simulatorPath, suspiciousPath, overwrite: true);

        var sourceManagedAssembly = Path.ChangeExtension(simulatorPath, ".dll");
        var copiedManagedAssembly = Path.Combine(labDirectory, Path.GetFileName(sourceManagedAssembly));
        if (File.Exists(sourceManagedAssembly) && !File.Exists(copiedManagedAssembly))
        {
            throw new IOException("No se copiaron todos los archivos necesarios para la simulación de ruta.");
        }

        return suspiciousPath;
    }

    // supervisa la ejecucion del simulador
    private async Task MonitorScenarioProcessAsync(Process process, ValidationScenario scenario, int expectedDurationSeconds)
    {
        try
        {
            await process.WaitForExitAsync();
            await FinalizeExitedScenarioAsync(process, scenario, expectedDurationSeconds, CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            bool isCurrentScenario;
            lock (_stateLock)
            {
                isCurrentScenario = ReferenceEquals(_activeProcess, process) && ReferenceEquals(_activeScenario, scenario);
            }

            if (isCurrentScenario)
            {
                await MarkTechnicalFailureAsync(scenario, $"No se pudo supervisar el simulador: {ex.Message}", CancellationToken.None);
            }
        }
    }

    // finaliza un simulador que termino
    private async Task FinalizeExitedScenarioAsync(
        Process process,
        ValidationScenario scenario,
        int expectedDurationSeconds,
        CancellationToken cancellationToken)
    {
        ScenarioTelemetryAccumulator? telemetry;
        lock (_stateLock)
        {
            if (!ReferenceEquals(_activeProcess, process) || !ReferenceEquals(_activeScenario, scenario))
            {
                return;
            }

            telemetry = _activeTelemetry;
            _activeProcess = null;
            _activeScenario = null;
            _activeTelemetry = null;
        }

        telemetry?.ApplyTo(scenario);
        scenario.FinishedAt = DateTime.Now;
        scenario.DurationSeconds = Math.Round((scenario.FinishedAt.Value - scenario.StartedAt).TotalSeconds, 2);
        var completionGraceSeconds = Math.Max(10, _settings.MonitoringIntervalSeconds * 2);
        var endedPrematurely = scenario.DurationSeconds < expectedDurationSeconds - completionGraceSeconds;

        if (!scenario.AlertGenerated && endedPrematurely)
        {
            var exitCode = SafeExitCode(process);
            scenario.ActualResult = $"El simulador terminó antes de completar la prueba (código de salida {exitCode}).";
            scenario.ActualClassification = "No evaluado";
            scenario.ConfusionResult = "Pendiente";
            scenario.Notes = $"{scenario.Notes} Fallo técnico del simulador; este resultado no se contabiliza como FN.";
        }
        else if (!scenario.AlertGenerated)
        {
            scenario.ActualResult = IsLegitimate(scenario)
                ? "Ventana legítima finalizada sin alertas."
                : "Finalizado sin alerta registrada durante la ventana de observación.";
            scenario.ActualClassification = "Sin alerta";
            scenario.ConfusionResult = IsLegitimate(scenario) ? "TN" : "FN";
            scenario.Notes = $"{scenario.Notes} Duración configurada completada automáticamente.";
        }
        else
        {
            scenario.Notes = $"{scenario.Notes} Duración configurada completada automáticamente.";
        }

        await _repository.UpdateValidationScenarioAsync(scenario, cancellationToken);
        process.Dispose();
    }

    // registra un fallo tecnico del escenario
    private async Task MarkTechnicalFailureAsync(
        ValidationScenario scenario,
        string result,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        ScenarioTelemetryAccumulator? telemetry = null;
        lock (_stateLock)
        {
            if (ReferenceEquals(_activeScenario, scenario))
            {
                process = _activeProcess;
                telemetry = _activeTelemetry;
                _activeProcess = null;
                _activeScenario = null;
                _activeTelemetry = null;
            }
        }

        telemetry?.ApplyTo(scenario);
        scenario.FinishedAt = DateTime.Now;
        scenario.DurationSeconds = Math.Round((scenario.FinishedAt.Value - scenario.StartedAt).TotalSeconds, 2);
        scenario.ActualResult = result;
        scenario.ActualClassification = "No evaluado";
        scenario.ConfusionResult = "Pendiente";
        scenario.Notes = $"{scenario.Notes} Fallo técnico del simulador; no se contabiliza como FN.";
        await _repository.UpdateValidationScenarioAsync(scenario, cancellationToken);
        if (process is { HasExited: false })
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // el proceso pudo terminar durante el registro del fallo
            }
        }

        process?.Dispose();
    }

    // obtiene el codigo de salida del proceso
    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch
        {
            return "no disponible";
        }
    }

    // acumula telemetria del escenario
    private sealed class ScenarioTelemetryAccumulator
    {
        private readonly bool _useSystemMetrics;
        private int _sampleCount;
        private double _cpuTotal;
        private double _maximumCpu;
        private double _memoryTotal;
        private double _maximumMemory;
        private double _networkTotal;
        private int _maximumNetwork;

        // crea el acumulador de telemetria
        public ScenarioTelemetryAccumulator(bool useSystemMetrics)
        {
            _useSystemMetrics = useSystemMetrics;
        }

        // agrega una muestra al acumulador
        public void Observe(TelemetrySnapshot snapshot, int? processId)
        {
            if (_useSystemMetrics)
            {
                AddSample(
                    snapshot.SystemMetric.CpuTotalPercent,
                    snapshot.SystemMetric.MemoryUsedMb,
                    snapshot.ProcessMetrics.Sum(metric => metric.NetworkConnectionCount));
                return;
            }

            if (processId is null)
            {
                return;
            }

            var processMetric = snapshot.ProcessMetrics.FirstOrDefault(metric => metric.ProcessId == processId.Value);
            if (processMetric is not null)
            {
                AddSample(processMetric.CpuPercent, processMetric.MemoryMb, processMetric.NetworkConnectionCount);
            }
        }

        // copia los datos al escenario
        public void ApplyTo(ValidationScenario scenario)
        {
            if (_sampleCount == 0)
            {
                return;
            }

            scenario.TelemetrySampleCount = _sampleCount;
            scenario.AverageCpuPercent = Math.Round(_cpuTotal / _sampleCount, 2);
            scenario.MaximumCpuPercent = Math.Round(_maximumCpu, 2);
            scenario.AverageMemoryMb = Math.Round(_memoryTotal / _sampleCount, 2);
            scenario.MaximumMemoryMb = Math.Round(_maximumMemory, 2);
            scenario.AverageNetworkConnections = Math.Round(_networkTotal / _sampleCount, 2);
            scenario.MaximumNetworkConnections = _maximumNetwork;
            if (string.IsNullOrWhiteSpace(scenario.ObservedActivity))
            {
                scenario.ObservedActivity = _useSystemMetrics
                    ? "Actividad general del sistema observada durante la ventana normal."
                    : "Telemetría del proceso controlado observada durante el escenario.";
            }
        }

        // actualiza los acumulados de una muestra
        private void AddSample(double cpuPercent, double memoryMb, int networkConnections)
        {
            _sampleCount++;
            _cpuTotal += cpuPercent;
            _maximumCpu = Math.Max(_maximumCpu, cpuPercent);
            _memoryTotal += memoryMb;
            _maximumMemory = Math.Max(_maximumMemory, memoryMb);
            _networkTotal += networkConnections;
            _maximumNetwork = Math.Max(_maximumNetwork, networkConnections);
        }
    }

    // busca el ejecutable del simulador
    private string? ResolveSimulatorPath()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.SimulatorExecutablePath))
        {
            candidates.Add(_settings.SimulatorExecutablePath);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "BotnetMiningDetector.Simulator.exe"));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\BotnetMiningDetector.Simulator\bin\Debug\net10.0\BotnetMiningDetector.Simulator.exe")));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\BotnetMiningDetector.Simulator\bin\Release\net10.0\BotnetMiningDetector.Simulator.exe")));

        return candidates.FirstOrDefault(File.Exists);
    }
}
