namespace BotnetMiningDetector.App.Common;

// configuracion general del agente
public sealed class AgentSettings
{
    public int MonitoringIntervalSeconds { get; set; } = 5;
    public int BaselineLearningMinutes { get; set; } = 2;
    public double BaselineMinimumSampleCoveragePercent { get; set; } = 80;
    public int SustainedCpuSeconds { get; set; } = 30;
    public int SustainedMemorySeconds { get; set; } = 30;
    public double AbsoluteCpuThresholdPercent { get; set; } = 80;
    public double CpuAbsoluteThresholdPercent { get; set; } = 80;
    public double MemoryAbsoluteThresholdMb { get; set; } = 1024;
    public double MemoryMinimumIncreaseMb { get; set; } = 25;
    public double MemoryBaselineHeadroomPercent { get; set; } = 15;
    public double NewProcessCpuThresholdPercent { get; set; } = 25;
    public double NewProcessMemoryThresholdMb { get; set; } = 500;
    public double StdDevMultiplier { get; set; } = 2;
    public double StandardDeviationMultiplier { get; set; } = 2;
    public double MinCpuThreshold { get; set; } = 15;
    public int LearningDurationMinutes { get; set; } = 2;
    public int PersistentProcessSeconds { get; set; } = 120;
    public int ProcessPersistenceSeconds { get; set; } = 120;
    public double HighMemoryMbThreshold { get; set; } = 500;
    public int PeriodicConnectionThreshold { get; set; } = 2;
    public int PeriodicObservationCount { get; set; } = 2;
    public int PeriodicActivityObservationCount { get; set; } = 2;
    public int AlertCooldownSeconds { get; set; } = 120;
    public bool RequireBaselineForDetection { get; set; } = true;
    public string CsvExportPath { get; set; } = "Exports";
    public string ExportPath { get; set; } = "Exports";
    public string DatabasePath { get; set; } = "agent_monitor.db";
    public string SimulatorExecutablePath { get; set; } = string.Empty;
    public int LocalhostBotnetPort { get; set; } = 5055;
    public bool EnableSustainedCpuRule { get; set; } = true;
    public bool EnableCpuRule { get; set; } = true;
    public bool EnableMemoryRule { get; set; } = true;
    public bool EnableNewProcessHighLoadRule { get; set; } = true;
    public bool EnableNewProcessRule { get; set; } = true;
    public bool EnablePersistentProcessRule { get; set; } = true;
    public bool EnableSuspiciousPathRule { get; set; } = true;
    public bool EnablePeriodicActivityRule { get; set; } = true;
    public bool EnableSignalCorrelationRule { get; set; } = true;
    public bool EnableCorrelationRule { get; set; } = true;
    public bool EnableGlobalCpuRule { get; set; } = true;
    public bool EnableGlobalMemoryRule { get; set; } = true;

    // ajusta valores relacionados de configuracion
    public void Normalize()
    {
        BaselineLearningMinutes = BaselineLearningMinutes <= 0 ? Math.Max(1, LearningDurationMinutes) : BaselineLearningMinutes;
        LearningDurationMinutes = BaselineLearningMinutes;
        BaselineMinimumSampleCoveragePercent = Math.Clamp(BaselineMinimumSampleCoveragePercent, 1, 100);
        CpuAbsoluteThresholdPercent = CpuAbsoluteThresholdPercent <= 0 ? AbsoluteCpuThresholdPercent : CpuAbsoluteThresholdPercent;
        AbsoluteCpuThresholdPercent = CpuAbsoluteThresholdPercent;
        StandardDeviationMultiplier = StandardDeviationMultiplier <= 0 ? StdDevMultiplier : StandardDeviationMultiplier;
        StdDevMultiplier = StandardDeviationMultiplier;
        ProcessPersistenceSeconds = ProcessPersistenceSeconds <= 0 ? PersistentProcessSeconds : ProcessPersistenceSeconds;
        PersistentProcessSeconds = ProcessPersistenceSeconds;
        NewProcessMemoryThresholdMb = NewProcessMemoryThresholdMb <= 0 ? HighMemoryMbThreshold : NewProcessMemoryThresholdMb;
        HighMemoryMbThreshold = NewProcessMemoryThresholdMb;
        MemoryMinimumIncreaseMb = Math.Max(0, MemoryMinimumIncreaseMb);
        MemoryBaselineHeadroomPercent = Math.Max(0, MemoryBaselineHeadroomPercent);
        PeriodicActivityObservationCount = PeriodicActivityObservationCount <= 0 ? PeriodicObservationCount : PeriodicActivityObservationCount;
        PeriodicObservationCount = PeriodicActivityObservationCount;
        ExportPath = string.IsNullOrWhiteSpace(ExportPath) ? CsvExportPath : ExportPath;
        CsvExportPath = ExportPath;
        EnableSustainedCpuRule = EnableCpuRule;
        EnableNewProcessHighLoadRule = EnableNewProcessRule;
        EnableSignalCorrelationRule = EnableCorrelationRule;
    }
}
