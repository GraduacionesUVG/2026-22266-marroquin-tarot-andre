using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BotnetMiningDetector.App.Agent;
using BotnetMiningDetector.App.Baseline;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;
using BotnetMiningDetector.App.Data.Models;
using BotnetMiningDetector.App.Detection;
using BotnetMiningDetector.App.Evaluation;
using BotnetMiningDetector.App.Export;
using BotnetMiningDetector.App.Simulation;

namespace BotnetMiningDetector.App.UI.ViewModels;

// modelo principal de la interfaz
public sealed class MainViewModel : ObservableObject
{
    private readonly AgentSettings _settings;
    private readonly ConfigurationService _configurationService;
    private readonly SqliteRepository _repository;
    private readonly MonitoringService _monitoringService;
    private readonly BaselineService _baselineService;
    private readonly DetectionEngine _detectionEngine;
    private readonly SimulationService _simulationService;
    private readonly CsvExportService _exportService;
    private readonly EvaluationMetricsCalculator _metricsCalculator;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private CancellationTokenSource? _monitoringCancellation;
    private CancellationTokenSource? _baselineCancellation;
    private Task? _monitoringTask;
    private BaselineProfile? _activeBaseline;
    private List<ProcessBaseline> _processBaselineCache = new();
    private bool _isInitialized;
    private AgentStatus _status = AgentStatus.Pausado;
    private double _cpuTotalPercent;
    private double _memoryUsedMb;
    private double _memoryTotalMb;
    private double _memoryUsedPercent;
    private int _processCount;
    private int _alertCount;
    private string _lastAlertText = "Sin alertas";
    private string _progressMessage = "Listo";
    private string _baselineSummary = "Sin línea base activa";
    private string _exportStatus = string.Empty;
    private EvaluationSummary? _evaluationSummary;
    private string _evaluationSummaryText = "Sin escenarios suficientes";
    private string _detectionReadinessText = "Detección no lista: construya una línea base";
    private string _activeScenarioText = "Sin escenario activo";
    private string _agentPerformanceText = "Agente: sin muestra local";

    // conecta los servicios de la aplicacion
    public MainViewModel(
        AgentSettings settings,
        ConfigurationService configurationService,
        SqliteRepository repository,
        MonitoringService monitoringService,
        BaselineService baselineService,
        DetectionEngine detectionEngine,
        SimulationService simulationService,
        CsvExportService exportService,
        EvaluationMetricsCalculator metricsCalculator)
    {
        _settings = settings;
        _configurationService = configurationService;
        _repository = repository;
        _monitoringService = monitoringService;
        _baselineService = baselineService;
        _detectionEngine = detectionEngine;
        _simulationService = simulationService;
        _exportService = exportService;
        _metricsCalculator = metricsCalculator;

        StartMonitoringCommand = new RelayCommand(StartMonitoringAsync);
        PauseMonitoringCommand = new RelayCommand(PauseMonitoringAsync);
        BuildBaselineCommand = new RelayCommand(BuildBaselineAsync);
        ExportCommand = new RelayCommand(ExportAsync);
        RefreshCommand = new RelayCommand(RefreshFromDatabaseAsync);
        ClearExperimentDataCommand = new RelayCommand(ClearExperimentDataAsync);
        RunNormalScenarioCommand = new RelayCommand(() => RunScenarioAsync("UsoNormal"));
        RunLegitimateLoadScenarioCommand = new RelayCommand(() => RunScenarioAsync("CargaLegitimaTemporal"));
        RunMiningScenarioCommand = new RelayCommand(() => RunScenarioAsync("CriptomineriaSimulada"));
        RunBotnetScenarioCommand = new RelayCommand(() => RunScenarioAsync("BotnetSimulada"));
        RunPersistentScenarioCommand = new RelayCommand(() => RunScenarioAsync("ProcesoPersistenteSimulado"));
        RunSuspiciousPathScenarioCommand = new RelayCommand(() => RunScenarioAsync("RutaSospechosaSimulada"));
        RunCombinedScenarioCommand = new RelayCommand(() => RunScenarioAsync("Combinado"));
        StopSimulationCommand = new RelayCommand(StopSimulationAsync);
        AcknowledgeAlertCommand = new RelayCommand(async parameter =>
        {
            if (parameter is Alert alert)
            {
                await AcknowledgeAlertAsync(alert);
            }
        });
    }

    public ObservableCollection<ProcessMetric> CurrentProcesses { get; } = new();
    public ObservableCollection<Alert> Alerts { get; } = new();
    public ObservableCollection<ProcessBaseline> ProcessBaselines { get; } = new();
    public ObservableCollection<ValidationScenario> ValidationScenarios { get; } = new();

    public ICommand StartMonitoringCommand { get; }
    public ICommand PauseMonitoringCommand { get; }
    public ICommand BuildBaselineCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ClearExperimentDataCommand { get; }
    public ICommand RunNormalScenarioCommand { get; }
    public ICommand RunLegitimateLoadScenarioCommand { get; }
    public ICommand RunMiningScenarioCommand { get; }
    public ICommand RunBotnetScenarioCommand { get; }
    public ICommand RunPersistentScenarioCommand { get; }
    public ICommand RunSuspiciousPathScenarioCommand { get; }
    public ICommand RunCombinedScenarioCommand { get; }
    public ICommand StopSimulationCommand { get; }
    public ICommand AcknowledgeAlertCommand { get; }

    public string StatusText => _status switch
    {
        AgentStatus.Activo => "Activo",
        AgentStatus.Aprendizaje => "Aprendizaje",
        _ => "Pausado"
    };

    public string CpuTotalText => $"{_cpuTotalPercent:N1}%";
    public string MemoryText => $"{_memoryUsedMb:N0} MB / {_memoryTotalMb:N0} MB ({_memoryUsedPercent:N1}%)";
    public int ProcessCount
    {
        get => _processCount;
        private set => SetProperty(ref _processCount, value);
    }

    public int AlertCount
    {
        get => _alertCount;
        private set => SetProperty(ref _alertCount, value);
    }

    public string LastAlertText
    {
        get => _lastAlertText;
        private set => SetProperty(ref _lastAlertText, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public string BaselineSummary
    {
        get => _baselineSummary;
        private set => SetProperty(ref _baselineSummary, value);
    }

    public string ExportStatus
    {
        get => _exportStatus;
        private set => SetProperty(ref _exportStatus, value);
    }

    public string EvaluationSummaryText
    {
        get => _evaluationSummaryText;
        private set => SetProperty(ref _evaluationSummaryText, value);
    }

    public EvaluationSummary? EvaluationSummary => _evaluationSummary;

    public string BaselineProcessExplanation => _activeBaseline is null
        ? "Sin línea base activa"
        : _processBaselineCache.Count == 0
            ? "Línea base activa sin procesos habituales cargados"
            : $"{_processBaselineCache.Count} procesos habituales aprendidos en la línea base";

    public string DetectionReadinessText
    {
        get => _detectionReadinessText;
        private set => SetProperty(ref _detectionReadinessText, value);
    }

    public string ActiveScenarioText
    {
        get => _activeScenarioText;
        private set => SetProperty(ref _activeScenarioText, value);
    }

    public BaselineProfile? ActiveBaseline => _activeBaseline;
    public string DatabasePath => _repository.DatabasePath;
    public AgentSettings Settings => _settings;
    public string AgentPerformanceText
    {
        get => _agentPerformanceText;
        private set => SetProperty(ref _agentPerformanceText, value);
    }

    public string SettingsSummary =>
        $"Intervalo: {_settings.MonitoringIntervalSeconds}s | CPU sostenida: {_settings.SustainedCpuSeconds}s | Umbral CPU: {_settings.CpuAbsoluteThresholdPercent:N0}% | Aprendizaje: {_settings.BaselineLearningMinutes} min";

    // crea una instancia local para pruebas
    public static MainViewModel CreateDefault()
    {
        var configuration = new ConfigurationService(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var settings = configuration.Load();
        var repository = new SqliteRepository(settings.DatabasePath);
        var systemReader = new SystemResourceReader();
        var networkReader = new NetworkConnectionReader();
        var processSampler = new ProcessSampler(systemReader, networkReader);
        var monitoringService = new MonitoringService(processSampler, repository);
        var baselineService = new BaselineService(monitoringService, repository);
        var detectionEngine = new DetectionEngine();
        var simulationService = new SimulationService(repository, settings);
        var metricsCalculator = new EvaluationMetricsCalculator();
        var exportService = new CsvExportService(repository, metricsCalculator);

        return new MainViewModel(
            settings,
            configuration,
            repository,
            monitoringService,
            baselineService,
            detectionEngine,
            simulationService,
            exportService,
            metricsCalculator);
    }

    // carga datos iniciales de la aplicacion
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await _repository.InitializeAsync();
            await RefreshFromDatabaseCoreAsync();
            _isInitialized = true;
            ProgressMessage = "Base local inicializada";
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    // detiene tareas activas al cerrar
    public async Task ShutdownAsync()
    {
        await PauseMonitoringAsync();
        await _simulationService.StopActiveScenarioAsync("Aplicacion cerrada.");
    }

    // inicia el monitoreo continuo
    public async Task StartMonitoringAsync()
    {
        await EnsureInitializedAsync();

        if (_monitoringTask is { IsCompleted: false })
        {
            return;
        }

        await LoadBaselineAsync();
        _monitoringCancellation = new CancellationTokenSource();
        SetStatus(AgentStatus.Activo);
        ProgressMessage = _activeBaseline is null || _processBaselineCache.Count == 0
            ? "Monitoreo activo sin alertas definitivas: falta línea base"
            : "Monitoreo activo con línea base";
        _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCancellation.Token));
    }

    // pausa el monitoreo continuo
    public async Task PauseMonitoringAsync()
    {
        if (_baselineCancellation is not null)
        {
            _baselineCancellation.Cancel();
            ProgressMessage = "Cancelando construcción de línea base";
            return;
        }

        if (_monitoringCancellation is null)
        {
            SetStatus(AgentStatus.Pausado);
            return;
        }

        _monitoringCancellation.Cancel();
        try
        {
            if (_monitoringTask is not null)
            {
                await _monitoringTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _monitoringCancellation.Dispose();
            _monitoringCancellation = null;
            _monitoringTask = null;
            SetStatus(AgentStatus.Pausado);
            ProgressMessage = "Monitoreo pausado";
        }
    }

    // construye una nueva linea base
    public async Task BuildBaselineAsync()
    {
        await EnsureInitializedAsync();
        if (_baselineCancellation is not null)
        {
            ProgressMessage = "Ya hay una construcción de línea base en curso";
            return;
        }

        await PauseMonitoringAsync();
        SetStatus(AgentStatus.Aprendizaje);
        ProgressMessage = "Construyendo línea base";

        var baselineCancellation = new CancellationTokenSource();
        _baselineCancellation = baselineCancellation;
        var progress = new Progress<BaselineProgress>(state =>
        {
            ProgressMessage = $"{state.Message}. Restante: {FormatRemainingTime(state.Remaining)}";
        });

        try
        {
            var result = await _baselineService.BuildBaselineAsync(_settings, progress, baselineCancellation.Token);
            _activeBaseline = result.Profile;
            _processBaselineCache = result.ProcessBaselines;
            await LoadBaselineAsync();
            ProgressMessage = "Línea base reconstruida";
        }
        catch (OperationCanceledException)
        {
            ProgressMessage = "Construcción de línea base cancelada";
        }
        catch (Exception ex)
        {
            ProgressMessage = $"Error construyendo línea base: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_baselineCancellation, baselineCancellation))
            {
                _baselineCancellation = null;
            }

            baselineCancellation.Dispose();
            SetStatus(AgentStatus.Pausado);
        }
    }

    // ejecuta el ciclo de monitoreo
    private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var knownNames = _processBaselineCache
                    .Select(process => process.ProcessName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var snapshot = await _monitoringService.CollectAndStoreAsync(knownNames, AgentStatus.Activo, cancellationToken);
                _simulationService.ObserveSnapshot(snapshot);
                var detection = _detectionEngine.Analyze(snapshot, _activeBaseline, _processBaselineCache, _settings);
                foreach (var alert in detection.Alerts)
                {
                    await _repository.InsertAlertAsync(alert, cancellationToken);
                }

                await _simulationService.NotifyAlertsAsync(detection.Alerts, cancellationToken);
                var alertCount = await _repository.GetAlertCountAsync(cancellationToken);
                var activeScenario = _simulationService.ActiveScenario;

                await RunOnUiAsync(() =>
                {
                    ApplySnapshot(snapshot);
                    foreach (var alert in detection.Alerts.OrderByDescending(alert => alert.Timestamp))
                    {
                        Alerts.Insert(0, alert);
                    }

                    AlertCount = alertCount;
                    if (Alerts.Count > 0)
                    {
                        LastAlertText = $"{Alerts[0].Timestamp:g} - {Alerts[0].Severity}: {Alerts[0].AlertType} ({Alerts[0].ProcessName})";
                    }

                    ActiveScenarioText = activeScenario is null
                        ? "Sin escenario activo"
                        : $"{activeScenario.Name} iniciado: {activeScenario.StartedAt:g}";
                    ProgressMessage = detection.Summary;
                });

                await RefreshScenariosAndEvaluationAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.MonitoringIntervalSeconds)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() => ProgressMessage = $"Error de monitoreo: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.MonitoringIntervalSeconds)), cancellationToken);
            }
        }
    }

    // inicia un escenario de validacion
    public async Task RunScenarioAsync(string scenarioType)
    {
        await EnsureInitializedAsync();
        await LoadBaselineAsync();
        if (_activeBaseline is null || _processBaselineCache.Count == 0)
        {
            ProgressMessage = "Construya una línea base antes de ejecutar escenarios de validación";
            return;
        }

        var scenario = await _simulationService.StartScenarioAsync(scenarioType);
        ActiveScenarioText = scenario.FinishedAt is null
            ? $"{scenario.Name} iniciado: {scenario.StartedAt:g}"
            : "Sin escenario activo";
        ProgressMessage = $"{scenario.Name}: {scenario.ActualResult}";
        await RefreshScenariosAndEvaluationAsync();
    }

    // detiene el escenario activo
    public async Task StopSimulationAsync()
    {
        await EnsureInitializedAsync();
        await _simulationService.StopActiveScenarioAsync();
        ActiveScenarioText = "Sin escenario activo";
        ProgressMessage = "Simulación detenida";
        await RefreshScenariosAndEvaluationAsync();
    }

    // exporta los datos de validacion
    public async Task ExportAsync()
    {
        await EnsureInitializedAsync();
        var directory = await _exportService.ExportAllAsync(_settings);
        ExportStatus = $"Exportado en: {directory}";
        ProgressMessage = "CSV generados";
        await RefreshScenariosAndEvaluationAsync();
    }

    // limpia resultados de experimentos
    public async Task ClearExperimentDataAsync()
    {
        await EnsureInitializedAsync();
        await PauseMonitoringAsync();
        await _simulationService.StopActiveScenarioAsync("Datos limpiados por el usuario.");
        await _repository.ClearExperimentDataAsync();
        CurrentProcesses.Clear();
        Alerts.Clear();
        ValidationScenarios.Clear();
        ProcessCount = 0;
        AlertCount = 0;
        LastAlertText = "Sin alertas";
        ActiveScenarioText = "Sin escenario activo";
        _evaluationSummary = null;
        OnPropertyChanged(nameof(EvaluationSummary));
        EvaluationSummaryText = "Sin escenarios suficientes";
        ProgressMessage = "Métricas, alertas y escenarios limpiados; línea base conservada";
        await LoadBaselineAsync();
    }

    // marca una alerta como revisada
    public async Task AcknowledgeAlertAsync(Alert alert)
    {
        await EnsureInitializedAsync();
        await _repository.AcknowledgeAlertAsync(alert.Id);
        alert.WasAcknowledged = true;
        await RefreshAlertsAsync();
        ProgressMessage = $"Alerta {alert.Id} marcada como revisada";
    }

    // actualiza la interfaz desde sqlite
    public async Task RefreshFromDatabaseAsync()
    {
        await EnsureInitializedAsync();
        await RefreshFromDatabaseCoreAsync();
    }

    // carga los datos principales desde sqlite
    private async Task RefreshFromDatabaseCoreAsync()
    {
        await LoadBaselineAsync();
        await RefreshAlertsAsync();
        await RefreshScenariosAndEvaluationAsync();
    }

    // asegura que el modelo este inicializado
    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await InitializeAsync();
    }

    // carga la linea base activa
    private async Task LoadBaselineAsync()
    {
        _activeBaseline = await _repository.GetActiveBaselineProfileAsync();
        _processBaselineCache = await _repository.GetProcessBaselinesAsync();

        await RunOnUiAsync(() =>
        {
            ProcessBaselines.Clear();
            foreach (var process in _processBaselineCache)
            {
                ProcessBaselines.Add(process);
            }

            BaselineSummary = _activeBaseline is null
                ? "Sin línea base activa"
                : $"Creada: {_activeBaseline.CreatedAt:g} | CPU media: {_activeBaseline.CpuAverage:N2}% +/- {_activeBaseline.CpuStdDev:N2} | rango {_activeBaseline.CpuMin:N2}% a {_activeBaseline.CpuMax:N2}% | Memoria media: {_activeBaseline.MemoryAverage:N2}% +/- {_activeBaseline.MemoryStdDev:N2} | rango {_activeBaseline.MemoryMin:N2}% a {_activeBaseline.MemoryMax:N2}% | TCP medio: {_activeBaseline.NetworkConnectionAverage:N2} | Procesos habituales: {_processBaselineCache.Count} | Cobertura de muestreo: {(_activeBaseline.ExpectedSampleCount == 0 ? "No disponible" : $"{_activeBaseline.SampleCoveragePercent:N1}%")}";
            DetectionReadinessText = _activeBaseline is null || _processBaselineCache.Count == 0
                ? "Detección no lista: construya una línea base"
                : $"Detección lista: {_processBaselineCache.Count} procesos habituales cargados desde la línea base";
            OnPropertyChanged(nameof(ActiveBaseline));
            OnPropertyChanged(nameof(BaselineProcessExplanation));
        });
    }

    // actualiza la lista de alertas
    private async Task RefreshAlertsAsync()
    {
        var alerts = await _repository.GetRecentAlertsAsync(200);
        var alertCount = await _repository.GetAlertCountAsync();

        await RunOnUiAsync(() =>
        {
            Alerts.Clear();
            foreach (var alert in alerts)
            {
                Alerts.Add(alert);
            }

            AlertCount = alertCount;
            LastAlertText = Alerts.Count == 0
                ? "Sin alertas"
                : $"{Alerts[0].Timestamp:g} - {Alerts[0].Severity}: {Alerts[0].AlertType} ({Alerts[0].ProcessName})";
        });
    }

    // actualiza escenarios y metricas
    private async Task RefreshScenariosAndEvaluationAsync(CancellationToken cancellationToken = default)
    {
    var scenarios = await _repository.GetValidationScenariosAsync(cancellationToken);
        var processMetrics = await _repository.GetProcessMetricsAsync(cancellationToken);
        var alerts = await _repository.GetRecentAlertsAsync(int.MaxValue, cancellationToken);
        var summary = _metricsCalculator.Calculate(scenarios, processMetrics, alerts);
        await _repository.SaveEvaluationSummaryAsync(summary, cancellationToken);

        await RunOnUiAsync(() =>
        {
            _evaluationSummary = summary;
            ValidationScenarios.Clear();
            foreach (var scenario in scenarios)
            {
                ValidationScenarios.Add(scenario);
            }

            EvaluationSummaryText =
                $"TP: {summary.TruePositives} | FP: {summary.FalsePositives} | FN: {summary.FalseNegatives} | TN: {summary.TrueNegatives} | Precisión: {summary.Precision:P1} | Sensibilidad / Recall: {summary.Recall:P1} | Tasa FP: {summary.FalsePositiveRate:P1} | Tiempo medio: {summary.AverageDetectionTimeSeconds:N1}s | Agente CPU: {summary.AgentAverageCpuPercent:N2}% | Agente RAM media: {summary.AgentAverageMemoryMb:N1} MB | Agente RAM máxima: {summary.AgentMaximumMemoryMb:N1} MB | Cobertura línea base: {summary.BaselineCoverage:P1} | Claridad alertas: {summary.AlertClarityScore:P1} | Estadísticas: {summary.StatisticalActivationCount} | Absolutas: {summary.AbsoluteActivationCount} | Heurísticas: {summary.HeuristicActivationCount} | Correlación: {summary.CorrelationActivationCount}";
            OnPropertyChanged(nameof(EvaluationSummary));
        });
    }

    // aplica una muestra a la interfaz
    private void ApplySnapshot(TelemetrySnapshot snapshot)
    {
        _cpuTotalPercent = snapshot.SystemMetric.CpuTotalPercent;
        _memoryUsedMb = snapshot.SystemMetric.MemoryUsedMb;
        _memoryTotalMb = snapshot.SystemMetric.MemoryTotalMb;
        _memoryUsedPercent = snapshot.SystemMetric.MemoryUsedPercent;
        ProcessCount = snapshot.ProcessMetrics.Count;
        CurrentProcesses.Clear();

        foreach (var process in snapshot.ProcessMetrics.Take(250))
        {
            CurrentProcesses.Add(process);
        }

        var agentProcess = snapshot.ProcessMetrics
            .Where(process => process.ProcessName.Contains("BotnetMiningDetector.App", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(process => process.CpuPercent)
            .FirstOrDefault();
        AgentPerformanceText = agentProcess is null
            ? "Agente: sin muestra local"
            : $"Agente: CPU {agentProcess.CpuPercent:N2}% | RAM {agentProcess.MemoryMb:N1} MB";

        OnPropertyChanged(nameof(CpuTotalText));
        OnPropertyChanged(nameof(MemoryText));
    }

    // actualiza el estado del agente
    private void SetStatus(AgentStatus status)
    {
        _status = status;
        OnPropertyChanged(nameof(StatusText));
    }

    // presenta el tiempo restante
    private static string FormatRemainingTime(TimeSpan remaining)
    {
        return remaining.TotalHours >= 1
            ? remaining.ToString(@"hh\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
    }

    // guarda la configuracion actual
    public Task SaveSettingsAsync()
    {
        _configurationService.Save(_settings);
        OnPropertyChanged(nameof(SettingsSummary));
        ProgressMessage = "Configuración guardada";
        return Task.CompletedTask;
    }

    // ejecuta una accion en la interfaz
    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
