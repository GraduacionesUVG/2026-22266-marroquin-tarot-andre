using BotnetMiningDetector.App.Agent;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Detection;

// motor de deteccion local
public sealed class DetectionEngine
{
    private static readonly string[] SensitiveWindowsProcessNames =
    [
        "svchost",
        "explorer",
        "winlogon",
        "lsass",
        "services",
        "csrss",
        "smss"
    ];

    private readonly Dictionary<int, ProcessSignalState> _statesByPid = new();
    private DateTime? _globalCpuSince;
    private DateTime? _globalMemorySince;
    private DateTime? _lastGlobalCpuAlertAt;
    private DateTime? _lastGlobalMemoryAlertAt;
    private bool _globalCpuEpisodeAlerted;
    private bool _globalMemoryEpisodeAlerted;

    // analiza una muestra contra la linea base
    public DetectionResult Analyze(
        TelemetrySnapshot snapshot,
        BaselineProfile? baselineProfile,
        IReadOnlyCollection<ProcessBaseline> processBaselines,
        AgentSettings settings)
    {
        settings.Normalize();
        var result = new DetectionResult();
        var knownProcessMap = processBaselines.ToDictionary(process => process.ProcessName, StringComparer.OrdinalIgnoreCase);
        var observedPids = snapshot.ProcessMetrics.Select(process => process.ProcessId).ToHashSet();
        var multiInstanceProcessNames = snapshot.ProcessMetrics
            .GroupBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasUsableBaseline = baselineProfile is not null && processBaselines.Count > 0;

        foreach (var process in snapshot.ProcessMetrics)
        {
            GetState(process, snapshot.Timestamp);
        }

        if (settings.RequireBaselineForDetection && !hasUsableBaseline)
        {
            RemoveInactiveStates(observedPids);
            result.Summary = "Monitoreando sin línea base activa; alertas definitivas deshabilitadas";
            return result;
        }

        if (baselineProfile is not null)
        {
            result.Alerts.AddRange(AnalyzeGlobalRules(snapshot, baselineProfile, settings));
        }

        foreach (var process in snapshot.ProcessMetrics)
        {
            if (ShouldSkipProcess(process))
            {
                continue;
            }

            var state = GetState(process, snapshot.Timestamp);
            var baseline = knownProcessMap.TryGetValue(process.ProcessName, out var processBaseline) ? processBaseline : null;
            var isKnown = baseline is not null || process.IsKnownProcess || IsLikelyTrustedLocalProcess(process);
            var observedSeconds = Math.Max(0, (snapshot.Timestamp - state.FirstSeen).TotalSeconds);
            var canUseIndividualStatisticalBaseline = !multiInstanceProcessNames.Contains(process.ProcessName);

            var processAlerts = AnalyzeProcessRules(
                process,
                baseline,
                isKnown,
                canUseIndividualStatisticalBaseline,
                observedSeconds,
                state,
                snapshot.Timestamp,
                settings);
            result.Alerts.AddRange(processAlerts);
        }

        RemoveInactiveStates(observedPids);
        result.Summary = result.Alerts.Count == 0 ? "Sin desviaciones relevantes" : $"{result.Alerts.Count} alerta(s) generadas";
        return result;
    }

    // evalua reglas globales del sistema
    private IEnumerable<Alert> AnalyzeGlobalRules(TelemetrySnapshot snapshot, BaselineProfile baseline, AgentSettings settings)
    {
        var alerts = new List<Alert>();
        var cpuThreshold = Math.Max(settings.MinCpuThreshold, baseline.CpuAverage + settings.StandardDeviationMultiplier * Math.Max(1, baseline.CpuStdDev));
        var globalCpuExceeded = settings.EnableGlobalCpuRule && snapshot.SystemMetric.CpuTotalPercent > cpuThreshold;
        _globalCpuSince = globalCpuExceeded ? _globalCpuSince ?? snapshot.Timestamp : null;
        if (!globalCpuExceeded)
        {
            _globalCpuEpisodeAlerted = false;
        }

        if (globalCpuExceeded &&
            _globalCpuSince is not null &&
            snapshot.Timestamp - _globalCpuSince.Value >= TimeSpan.FromSeconds(settings.SustainedCpuSeconds) &&
            !_globalCpuEpisodeAlerted &&
            CanAlert(_lastGlobalCpuAlertAt, snapshot.Timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Media",
                alertType: "CPU global del sistema",
                ruleName: "CpuGlobalSistema",
                ruleCategory: "Cpu",
                thresholdType: "Statistical",
                processId: null,
                processName: "Sistema",
                observedValue: snapshot.SystemMetric.CpuTotalPercent,
                baselineAverage: baseline.CpuAverage,
                baselineStdDev: baseline.CpuStdDev,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: cpuThreshold,
                absoluteThreshold: null,
                sustainedSeconds: (snapshot.Timestamp - _globalCpuSince.Value).TotalSeconds,
                metricObserved: $"CPU total observada: {snapshot.SystemMetric.CpuTotalPercent:N2}%",
                baselineReference: $"CPU base del sistema: media {baseline.CpuAverage:N2}%, desviación {baseline.CpuStdDev:N2}%, umbral calculado {cpuThreshold:N2}%",
                description: "El uso total de CPU del sistema se mantuvo por encima de la línea base. Esta señal no identifica por sí sola un proceso responsable, pero ayuda a contextualizar otros eventos.",
                recommendation: "Revise los procesos con mayor CPU y contraste si existe una simulación activa o una tarea legítima intensiva.",
                evidenceDetails: $"Fórmula: max({settings.MinCpuThreshold:N2}, {baseline.CpuAverage:N2} + {settings.StandardDeviationMultiplier:N2} * max(1, {baseline.CpuStdDev:N2})) = {cpuThreshold:N2}."));
            _lastGlobalCpuAlertAt = snapshot.Timestamp;
            _globalCpuEpisodeAlerted = true;
        }

        var memoryThreshold = baseline.MemoryAverage + settings.StandardDeviationMultiplier * Math.Max(1, baseline.MemoryStdDev);
        var globalMemoryExceeded = settings.EnableGlobalMemoryRule && snapshot.SystemMetric.MemoryUsedPercent > memoryThreshold;
        _globalMemorySince = globalMemoryExceeded ? _globalMemorySince ?? snapshot.Timestamp : null;
        if (!globalMemoryExceeded)
        {
            _globalMemoryEpisodeAlerted = false;
        }

        if (globalMemoryExceeded &&
            _globalMemorySince is not null &&
            snapshot.Timestamp - _globalMemorySince.Value >= TimeSpan.FromSeconds(settings.SustainedMemorySeconds) &&
            !_globalMemoryEpisodeAlerted &&
            CanAlert(_lastGlobalMemoryAlertAt, snapshot.Timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Baja",
                alertType: "Memoria global del sistema",
                ruleName: "MemoriaGlobalSistema",
                ruleCategory: "Memory",
                thresholdType: "Statistical",
                processId: null,
                processName: "Sistema",
                observedValue: snapshot.SystemMetric.MemoryUsedPercent,
                baselineAverage: baseline.MemoryAverage,
                baselineStdDev: baseline.MemoryStdDev,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: memoryThreshold,
                absoluteThreshold: null,
                sustainedSeconds: (snapshot.Timestamp - _globalMemorySince.Value).TotalSeconds,
                metricObserved: $"Memoria usada observada: {snapshot.SystemMetric.MemoryUsedPercent:N2}%",
                baselineReference: $"Memoria base del sistema: media {baseline.MemoryAverage:N2}%, desviación {baseline.MemoryStdDev:N2}%, umbral calculado {memoryThreshold:N2}%",
                description: "El uso global de memoria se mantuvo por encima de la línea base. Esta señal sirve como contexto para analizar procesos individuales.",
                recommendation: "Revise procesos con uso alto de memoria y descarte actividad legítima como máquinas virtuales, navegadores o editores.",
                evidenceDetails: $"Fórmula: {baseline.MemoryAverage:N2} + {settings.StandardDeviationMultiplier:N2} * max(1, {baseline.MemoryStdDev:N2}) = {memoryThreshold:N2}."));
            _lastGlobalMemoryAlertAt = snapshot.Timestamp;
            _globalMemoryEpisodeAlerted = true;
        }

        return alerts;
    }

    // evalua reglas por proceso
    private IEnumerable<Alert> AnalyzeProcessRules(
        ProcessMetric process,
        ProcessBaseline? baseline,
        bool isKnown,
        bool canUseIndividualStatisticalBaseline,
        double observedSeconds,
        ProcessSignalState state,
        DateTime timestamp,
        AgentSettings settings)
    {
        var alerts = new List<Alert>();
        var signals = new List<string>();

        double? cpuStatThreshold = baseline is null ? null : Math.Max(settings.MinCpuThreshold, baseline.AverageCpu + settings.StandardDeviationMultiplier * Math.Max(1, baseline.StdDevCpu));
        var cpuStatExceeded = settings.EnableCpuRule && canUseIndividualStatisticalBaseline && baseline is not null && cpuStatThreshold is not null && process.CpuPercent > cpuStatThreshold.Value;
        var cpuAbsExceeded = settings.EnableCpuRule && process.CpuPercent > settings.CpuAbsoluteThresholdPercent;
        var cpuNewExceeded = !isKnown && process.CpuPercent > settings.NewProcessCpuThresholdPercent;

        UpdateAlertEpisode(state, "CpuProcesoSostenida", cpuStatExceeded);
        UpdateAlertEpisode(state, "CpuAbsoluta", cpuAbsExceeded);

        state.CpuStatSince = cpuStatExceeded ? state.CpuStatSince ?? timestamp : null;
        state.CpuAbsoluteSince = cpuAbsExceeded ? state.CpuAbsoluteSince ?? timestamp : null;

        if (cpuStatExceeded)
        {
            signals.Add("CPU estadística");
        }

        if (cpuAbsExceeded || cpuNewExceeded)
        {
            signals.Add("CPU alta");
        }

        if (cpuStatExceeded &&
            state.CpuStatSince is not null &&
            timestamp - state.CpuStatSince.Value >= TimeSpan.FromSeconds(settings.SustainedCpuSeconds) &&
            CanAlert(state, "CpuProcesoSostenida", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Alta",
                alertType: "Uso sostenido de CPU",
                ruleName: "CpuProcesoSostenida",
                ruleCategory: "Cpu",
                thresholdType: "Statistical",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: process.CpuPercent,
                baselineAverage: baseline!.AverageCpu,
                baselineStdDev: baseline.StdDevCpu,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: cpuStatThreshold,
                absoluteThreshold: settings.CpuAbsoluteThresholdPercent,
                sustainedSeconds: (timestamp - state.CpuStatSince.Value).TotalSeconds,
                metricObserved: $"CPU observada: {process.CpuPercent:N2}%",
                baselineReference: $"Base {baseline.ProcessName}: media CPU {baseline.AverageCpu:N2}%, desviación {baseline.StdDevCpu:N2}%, umbral calculado {cpuStatThreshold:N2}%",
                description: $"El proceso {process.ProcessName} mantuvo CPU por encima de su comportamiento normal. Este patrón puede ser compatible con abuso de recursos, como criptominería no autorizada, pero no confirma malware por sí solo.",
                recommendation: "Revise si reconoce este proceso. Si no es esperado, observe si el consumo continúa y considere ejecutar su antivirus o buscar apoyo técnico.",
                evidenceDetails: $"Fórmula estadística: max({settings.MinCpuThreshold:N2}, {baseline.AverageCpu:N2} + {settings.StandardDeviationMultiplier:N2} * max(1, {baseline.StdDevCpu:N2})) = {cpuStatThreshold:N2}."));
            MarkAlert(state, "CpuProcesoSostenida", timestamp);
        }

        if (cpuAbsExceeded &&
            state.CpuAbsoluteSince is not null &&
            timestamp - state.CpuAbsoluteSince.Value >= TimeSpan.FromSeconds(settings.SustainedCpuSeconds) &&
            CanAlert(state, "CpuAbsoluta", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Alta",
                alertType: "CPU absoluta",
                ruleName: "CpuAbsoluta",
                ruleCategory: "Cpu",
                thresholdType: "Absolute",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: process.CpuPercent,
                baselineAverage: baseline?.AverageCpu,
                baselineStdDev: baseline?.StdDevCpu,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: null,
                absoluteThreshold: settings.CpuAbsoluteThresholdPercent,
                sustainedSeconds: (timestamp - state.CpuAbsoluteSince.Value).TotalSeconds,
                metricObserved: $"CPU observada: {process.CpuPercent:N2}%",
                baselineReference: $"Umbral absoluto configurado: {settings.CpuAbsoluteThresholdPercent:N2}%",
                description: $"El proceso {process.ProcessName} supero el umbral absoluto de CPU durante el tiempo configurado.",
                recommendation: "Confirme si pertenece a una simulación controlada o a una tarea legítima intensiva.",
                evidenceDetails: $"Condicion: CPU {process.CpuPercent:N2}% > {settings.CpuAbsoluteThresholdPercent:N2}% durante al menos {settings.SustainedCpuSeconds}s."));
            MarkAlert(state, "CpuAbsoluta", timestamp);
        }

        double? memoryStatisticalThreshold = baseline is null
            ? null
            : baseline.AverageMemory + settings.StandardDeviationMultiplier * Math.Max(1, baseline.StdDevMemory);
        double? memoryPracticalThreshold = baseline is null
            ? null
            : baseline.AverageMemory + Math.Max(
                settings.MemoryMinimumIncreaseMb,
                baseline.AverageMemory * settings.MemoryBaselineHeadroomPercent / 100d);
        double? memoryStatThreshold = memoryStatisticalThreshold is null || memoryPracticalThreshold is null
            ? null
            : Math.Max(memoryStatisticalThreshold.Value, memoryPracticalThreshold.Value);
        var memoryStatExceeded = settings.EnableMemoryRule &&
            canUseIndividualStatisticalBaseline &&
            baseline is not null &&
            memoryStatThreshold is not null &&
            process.MemoryMb > memoryStatThreshold.Value;
        var memoryAbsExceeded = settings.EnableMemoryRule && process.MemoryMb > settings.MemoryAbsoluteThresholdMb;
        var memoryNewExceeded = !isKnown && process.MemoryMb > settings.NewProcessMemoryThresholdMb;

        UpdateAlertEpisode(state, "MemoriaProcesoSostenida", memoryStatExceeded);
        UpdateAlertEpisode(state, "MemoriaAbsoluta", memoryAbsExceeded);

        state.MemoryStatSince = memoryStatExceeded ? state.MemoryStatSince ?? timestamp : null;
        state.MemoryAbsoluteSince = memoryAbsExceeded ? state.MemoryAbsoluteSince ?? timestamp : null;

        if (memoryStatExceeded || memoryAbsExceeded || memoryNewExceeded)
        {
            signals.Add("Memoria alta");
        }

        if (memoryStatExceeded &&
            state.MemoryStatSince is not null &&
            timestamp - state.MemoryStatSince.Value >= TimeSpan.FromSeconds(settings.SustainedMemorySeconds) &&
            CanAlert(state, "MemoriaProcesoSostenida", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Baja",
                alertType: "Memoria fuera de línea base",
                ruleName: "MemoriaProcesoSostenida",
                ruleCategory: "Memory",
                thresholdType: "Statistical",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: process.MemoryMb,
                baselineAverage: baseline!.AverageMemory,
                baselineStdDev: baseline.StdDevMemory,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: memoryStatThreshold,
                absoluteThreshold: settings.MemoryAbsoluteThresholdMb,
                sustainedSeconds: (timestamp - state.MemoryStatSince.Value).TotalSeconds,
                metricObserved: $"Memoria observada: {process.MemoryMb:N2} MB",
                baselineReference: $"Base {baseline.ProcessName}: media memoria {baseline.AverageMemory:N2} MB, desviación {baseline.StdDevMemory:N2} MB, umbral estadístico {memoryStatisticalThreshold:N2} MB, umbral efectivo calibrado {memoryStatThreshold:N2} MB",
                description: $"El proceso {process.ProcessName} mantuvo uso de memoria por encima de su comportamiento normal.",
                recommendation: "Revise si el consumo corresponde a una actividad esperada o a una simulación controlada.",
                evidenceDetails: $"Umbral estadístico: {baseline.AverageMemory:N2} + {settings.StandardDeviationMultiplier:N2} * max(1, {baseline.StdDevMemory:N2}) = {memoryStatisticalThreshold:N2} MB. Margen operativo: max({settings.MemoryMinimumIncreaseMb:N2} MB, {settings.MemoryBaselineHeadroomPercent:N2}% de la media). Umbral efectivo: {memoryStatThreshold:N2} MB."));
            MarkAlert(state, "MemoriaProcesoSostenida", timestamp);
        }

        if (memoryAbsExceeded &&
            state.MemoryAbsoluteSince is not null &&
            timestamp - state.MemoryAbsoluteSince.Value >= TimeSpan.FromSeconds(settings.SustainedMemorySeconds) &&
            CanAlert(state, "MemoriaAbsoluta", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Media",
                alertType: "Memoria absoluta",
                ruleName: "MemoriaAbsoluta",
                ruleCategory: "Memory",
                thresholdType: "Absolute",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: process.MemoryMb,
                baselineAverage: baseline?.AverageMemory,
                baselineStdDev: baseline?.StdDevMemory,
                multiplier: settings.StandardDeviationMultiplier,
                calculatedThreshold: null,
                absoluteThreshold: settings.MemoryAbsoluteThresholdMb,
                sustainedSeconds: (timestamp - state.MemoryAbsoluteSince.Value).TotalSeconds,
                metricObserved: $"Memoria observada: {process.MemoryMb:N2} MB",
                baselineReference: $"Umbral absoluto configurado: {settings.MemoryAbsoluteThresholdMb:N2} MB",
                description: $"El proceso {process.ProcessName} supero el umbral absoluto de memoria durante el tiempo configurado.",
                recommendation: "Verifique si el consumo de memoria era esperado para la aplicación observada.",
                evidenceDetails: $"Condicion: memoria {process.MemoryMb:N2} MB > {settings.MemoryAbsoluteThresholdMb:N2} MB durante al menos {settings.SustainedMemorySeconds}s."));
            MarkAlert(state, "MemoriaAbsoluta", timestamp);
        }

        if (!isKnown)
        {
            signals.Add("Proceso nuevo");
        }

        var newProcessHighLoad = settings.EnableNewProcessRule && !isKnown && (cpuNewExceeded || memoryNewExceeded);
        UpdateAlertEpisode(state, "ProcesoNuevoAltaCarga", newProcessHighLoad);

        if (newProcessHighLoad &&
            observedSeconds >= Math.Min(15, settings.SustainedCpuSeconds) &&
            CanAlert(state, "ProcesoNuevoAltaCarga", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Alta",
                alertType: "Proceso nuevo con alta carga",
                ruleName: "ProcesoNuevoAltaCarga",
                ruleCategory: cpuNewExceeded ? "Cpu" : "Memory",
                thresholdType: "Heuristic",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: cpuNewExceeded ? process.CpuPercent : process.MemoryMb,
                baselineAverage: null,
                baselineStdDev: null,
                multiplier: null,
                calculatedThreshold: null,
                absoluteThreshold: cpuNewExceeded ? settings.NewProcessCpuThresholdPercent : settings.NewProcessMemoryThresholdMb,
                sustainedSeconds: observedSeconds,
                metricObserved: cpuNewExceeded ? $"CPU observada: {process.CpuPercent:N2}%" : $"Memoria observada: {process.MemoryMb:N2} MB",
                baselineReference: $"Proceso no registrado como habitual. Umbrales heurísticos: CPU {settings.NewProcessCpuThresholdPercent:N2}%, memoria {settings.NewProcessMemoryThresholdMb:N2} MB",
                description: $"Apareció un proceso no habitual llamado {process.ProcessName} con consumo relevante después de construir la línea base.",
                recommendation: "Revise si reconoce este proceso. Si no reconoce esta aplicación, busque apoyo técnico o ejecute su antivirus.",
                evidenceDetails: $"Tiempo observado por el agente: {observedSeconds:N2}s. Ruta: {process.ExecutablePath}."));
            MarkAlert(state, "ProcesoNuevoAltaCarga", timestamp);
        }

        var networkConnectionThreshold = CalculateNetworkConnectionThreshold(baseline, settings);
        var periodicActivity = HasPeriodicActivity(process, state, isKnown, networkConnectionThreshold, settings);
        UpdateAlertEpisode(state, "ActividadPeriodicaSimulada", periodicActivity);
        if (periodicActivity)
        {
            signals.Add("Actividad periódica");
        }

        if (settings.EnablePeriodicActivityRule &&
            periodicActivity &&
            CanAlert(state, "ActividadPeriodicaSimulada", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Media",
                alertType: "Actividad periódica simulada",
                ruleName: "ActividadPeriodicaSimulada",
                ruleCategory: "Network",
                thresholdType: "Heuristic",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: process.NetworkConnectionCount,
                baselineAverage: null,
                baselineStdDev: null,
                multiplier: null,
                calculatedThreshold: null,
                absoluteThreshold: networkConnectionThreshold,
                sustainedSeconds: observedSeconds,
                metricObserved: $"Conexiones TCP activas observadas: {process.NetworkConnectionCount}",
                baselineReference: baseline is null
                    ? $"Regla heurística: actividad periódica observada durante {state.PeriodicActivityObservations} muestra(s). Mínimo requerido: {settings.PeriodicActivityObservationCount}. Conexiones mínimas por muestra: {networkConnectionThreshold:N2}"
                    : $"Base {baseline.ProcessName}: TCP media {baseline.AverageNetworkConnections:N2}, desviación {baseline.StdDevNetworkConnections:N2}, umbral efectivo {networkConnectionThreshold:N2}",
                description: $"El proceso {process.ProcessName} presenta actividad TCP repetida. Esto simula periodicidad asociada a botnets sin contactar infraestructura externa.",
                recommendation: "Confirme si corresponde a una simulación de validación. En un caso real, revise si reconoce la aplicación y observe si la actividad se mantiene.",
                evidenceDetails: $"Tipo de regla: heurística con apoyo de línea base de red cuando existe. Evidencia: {process.NetworkConnectionCount} conexión(es) TCP activa(s), {state.PeriodicActivityObservations} muestra(s) periódica(s), umbral efectivo {networkConnectionThreshold:N2}, mínimo {settings.PeriodicActivityObservationCount} muestra(s). Ruta: {process.ExecutablePath}."));
            MarkAlert(state, "ActividadPeriodicaSimulada", timestamp);
        }

        var suspiciousPath = settings.EnableSuspiciousPathRule && HasSuspiciousWindowsLikePath(process);
        UpdateAlertEpisode(state, "RutaNoEstandar", suspiciousPath);
        if (suspiciousPath)
        {
            signals.Add("Ruta sospechosa");
        }

        if (suspiciousPath && CanAlert(state, "RutaNoEstandar", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Media",
                alertType: "Ruta sospechosa",
                ruleName: "RutaNoEstandar",
                ruleCategory: "SuspiciousPath",
                thresholdType: "Heuristic",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: null,
                baselineAverage: null,
                baselineStdDev: null,
                multiplier: null,
                calculatedThreshold: null,
                absoluteThreshold: null,
                sustainedSeconds: observedSeconds,
                metricObserved: process.ExecutablePath,
                baselineReference: "Procesos criticos de Windows normalmente se ejecutan desde C:\\Windows o C:\\Windows\\System32",
                description: $"El proceso {process.ProcessName} se parece a un proceso conocido de Windows, pero su ruta no coincide con una ubicación esperada.",
                recommendation: "Verifique la ubicación del archivo antes de confiar en el proceso. Si no reconoce esta aplicación, busque apoyo técnico.",
                evidenceDetails: $"Nombre sensible detectado: {process.ProcessName}. Ruta observada: {process.ExecutablePath}."));
            MarkAlert(state, "RutaNoEstandar", timestamp);
        }

        var persistentSignal = !isKnown && observedSeconds >= settings.ProcessPersistenceSeconds && (cpuNewExceeded || memoryNewExceeded || periodicActivity || suspiciousPath);
        UpdateAlertEpisode(state, "ProcesoPersistenteNoHabitual", persistentSignal);
        if (persistentSignal)
        {
            signals.Add("Persistencia");
        }

        if (settings.EnablePersistentProcessRule &&
            persistentSignal &&
            CanAlert(state, "ProcesoPersistenteNoHabitual", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Media",
                alertType: "Proceso persistente",
                ruleName: "ProcesoPersistenteNoHabitual",
                ruleCategory: "Persistence",
                thresholdType: "Heuristic",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: observedSeconds,
                baselineAverage: null,
                baselineStdDev: null,
                multiplier: null,
                calculatedThreshold: null,
                absoluteThreshold: settings.ProcessPersistenceSeconds,
                sustainedSeconds: observedSeconds,
                metricObserved: $"Tiempo observado: {observedSeconds:N0}s, CPU: {process.CpuPercent:N2}%, memoria: {process.MemoryMb:N2} MB, conexiones: {process.NetworkConnectionCount}",
                baselineReference: $"Proceso no habitual con permanencia mayor o igual a {settings.ProcessPersistenceSeconds}s",
                description: $"El proceso {process.ProcessName} no forma parte de la línea base y permanece activo con actividad observable.",
                recommendation: "Revise si el proceso corresponde a una tarea esperada. Observe si continúa durante varios minutos y considere ejecutar su antivirus.",
                evidenceDetails: $"Primera observación del agente: {state.FirstSeen:O}. Ruta: {process.ExecutablePath}."));
            MarkAlert(state, "ProcesoPersistenteNoHabitual", timestamp);
        }

        var distinctSignals = signals.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hasCorrelatedSignals = settings.EnableCorrelationRule && distinctSignals.Count >= 2;
        UpdateAlertEpisode(state, "CorrelacionSenales", hasCorrelatedSignals);

        if (hasCorrelatedSignals &&
            CanAlert(state, "CorrelacionSenales", timestamp, settings))
        {
            alerts.Add(CreateAlert(
                severity: "Alta",
                alertType: "Correlación de señales",
                ruleName: "CorrelacionSenales",
                ruleCategory: "Correlation",
                thresholdType: "Correlation",
                processId: process.ProcessId,
                processName: process.ProcessName,
                observedValue: distinctSignals.Count,
                baselineAverage: null,
                baselineStdDev: null,
                multiplier: null,
                calculatedThreshold: null,
                absoluteThreshold: null,
                sustainedSeconds: observedSeconds,
                metricObserved: $"Señales correlacionadas: {distinctSignals.Count}. CPU: {process.CpuPercent:N2}%, memoria: {process.MemoryMb:N2} MB, conexiones: {process.NetworkConnectionCount}",
                baselineReference: "Proceso comparado contra línea base local, umbrales y reglas activas",
                description: $"El proceso {process.ProcessName} combina varias señales de desviación, por lo que la severidad se eleva.",
                recommendation: "Priorice la revisión de este proceso y documente la evidencia para el análisis de la prueba.",
                evidenceDetails: $"Señales coincidentes: {string.Join(", ", distinctSignals)}."));
            MarkAlert(state, "CorrelacionSenales", timestamp);
        }

        return alerts;
    }

    // crea una alerta con evidencia
    private static Alert CreateAlert(
        string severity,
        string alertType,
        string ruleName,
        string ruleCategory,
        string thresholdType,
        int? processId,
        string processName,
        double? observedValue,
        double? baselineAverage,
        double? baselineStdDev,
        double? multiplier,
        double? calculatedThreshold,
        double? absoluteThreshold,
        double? sustainedSeconds,
        string metricObserved,
        string baselineReference,
        string description,
        string recommendation,
        string evidenceDetails)
    {
        return new Alert
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            AlertType = alertType,
            ProcessId = processId,
            ProcessName = processName,
            MetricObserved = metricObserved,
            BaselineReference = baselineReference,
            Description = description,
            Recommendation = recommendation,
            ObservedValue = observedValue,
            BaselineAverage = baselineAverage,
            BaselineStdDev = baselineStdDev,
            StdDevMultiplier = multiplier,
            CalculatedThreshold = calculatedThreshold,
            AbsoluteThreshold = absoluteThreshold,
            ThresholdType = thresholdType,
            SustainedDurationSeconds = sustainedSeconds,
            RuleName = ruleName,
            RuleCategory = ruleCategory,
            EvidenceDetails = evidenceDetails
        };
    }

    // calcula el umbral de conexiones
    private static double CalculateNetworkConnectionThreshold(ProcessBaseline? baseline, AgentSettings settings)
    {
        if (baseline is null)
        {
            return settings.PeriodicConnectionThreshold;
        }

        var statisticalThreshold = baseline.AverageNetworkConnections + settings.StandardDeviationMultiplier * Math.Max(1, baseline.StdDevNetworkConnections);
        return Math.Max(settings.PeriodicConnectionThreshold, statisticalThreshold);
    }

    // identifica actividad periodica
    private static bool HasPeriodicActivity(
        ProcessMetric process,
        ProcessSignalState state,
        bool isKnown,
        double connectionThreshold,
        AgentSettings settings)
    {
        var candidate = !isKnown && process.NetworkConnectionCount >= connectionThreshold;
        if (candidate)
        {
            state.PeriodicActivityObservations++;
        }
        else
        {
            state.PeriodicActivityObservations = 0;
        }

        return state.PeriodicActivityObservations >= settings.PeriodicActivityObservationCount &&
            process.NetworkConnectionCount >= connectionThreshold;
    }

    // identifica rutas parecidas a windows
    private static bool HasSuspiciousWindowsLikePath(ProcessMetric process)
    {
        if (string.IsNullOrWhiteSpace(process.ExecutablePath) ||
            process.ExecutablePath.Equals("No disponible", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = process.ProcessName.ToLowerInvariant().Replace(".exe", string.Empty);
        if (!SensitiveWindowsProcessNames.Any(known => name.Equals(known, StringComparison.OrdinalIgnoreCase) || name.Contains(known, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var path = process.ExecutablePath.ToLowerInvariant();
        var expectedWindowsPath = path.StartsWith(@"c:\windows\", StringComparison.OrdinalIgnoreCase);
        var suspiciousUserPath =
            path.Contains(@"\appdata\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\temp\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\downloads\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\desktop\", StringComparison.OrdinalIgnoreCase);

        return !expectedWindowsPath || suspiciousUserPath;
    }

    // obtiene el estado de un proceso
    private ProcessSignalState GetState(ProcessMetric process, DateTime timestamp)
    {
        if (_statesByPid.TryGetValue(process.ProcessId, out var state))
        {
            state.LastSeen = timestamp;
            return state;
        }

        state = new ProcessSignalState
        {
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            FirstSeen = timestamp,
            LastSeen = timestamp
        };
        _statesByPid[process.ProcessId] = state;
        return state;
    }

    // elimina estados de procesos ausentes
    private void RemoveInactiveStates(HashSet<int> observedPids)
    {
        var inactive = _statesByPid.Keys.Where(pid => !observedPids.Contains(pid)).ToList();
        foreach (var pid in inactive)
        {
            _statesByPid.Remove(pid);
        }
    }

    // filtra procesos que no deben analizarse
    private static bool ShouldSkipProcess(ProcessMetric process)
    {
        return process.ProcessId == Environment.ProcessId ||
            process.ProcessId <= 4 ||
            process.ProcessName.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase);
    }

    // identifica procesos locales confiables
    private static bool IsLikelyTrustedLocalProcess(ProcessMetric process)
    {
        if (string.IsNullOrWhiteSpace(process.ExecutablePath) ||
            process.ExecutablePath.Equals("No disponible", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return process.ExecutablePath.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) ||
            process.ExecutablePath.StartsWith(@"C:\Program Files\", StringComparison.OrdinalIgnoreCase) ||
            process.ExecutablePath.StartsWith(@"C:\Program Files (x86)\", StringComparison.OrdinalIgnoreCase);
    }

    // valida el tiempo entre alertas globales
    private static bool CanAlert(DateTime? lastAlertAt, DateTime timestamp, AgentSettings settings)
    {
        return lastAlertAt is null || timestamp - lastAlertAt.Value >= TimeSpan.FromSeconds(settings.AlertCooldownSeconds);
    }

    // valida el tiempo entre alertas de proceso
    private static bool CanAlert(ProcessSignalState state, string ruleName, DateTime timestamp, AgentSettings settings)
    {
        if (state.ActiveAlertEpisodes.Contains(ruleName))
        {
            return false;
        }

        return !state.LastAlertByRule.TryGetValue(ruleName, out var lastAlert) ||
            timestamp - lastAlert >= TimeSpan.FromSeconds(settings.AlertCooldownSeconds);
    }

    // registra una alerta en el estado
    private static void MarkAlert(ProcessSignalState state, string ruleName, DateTime timestamp)
    {
        state.LastAlertByRule[ruleName] = timestamp;
        state.ActiveAlertEpisodes.Add(ruleName);
    }

    // actualiza un episodio de alerta
    private static void UpdateAlertEpisode(ProcessSignalState state, string ruleName, bool conditionActive)
    {
        if (!conditionActive)
        {
            state.ActiveAlertEpisodes.Remove(ruleName);
        }
    }

    // estado de senales por proceso
    private sealed class ProcessSignalState
    {
        public int ProcessId
        {
            get; init;
        }
        public string ProcessName { get; init; } = string.Empty;
        public DateTime FirstSeen
        {
            get; init;
        }
        public DateTime LastSeen
        {
            get; set;
        }
        public DateTime? CpuStatSince
        {
            get; set;
        }
        public DateTime? CpuAbsoluteSince
        {
            get; set;
        }
        public DateTime? MemoryStatSince
        {
            get; set;
        }
        public DateTime? MemoryAbsoluteSince
        {
            get; set;
        }
        public int PeriodicActivityObservations
        {
            get; set;
        }
        public Dictionary<string, DateTime> LastAlertByRule { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ActiveAlertEpisodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
