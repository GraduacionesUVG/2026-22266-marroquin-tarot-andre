namespace BotnetMiningDetector.App.Data.Models;

// perfil global de la linea base
public sealed class BaselineProfile
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public double CpuAverage { get; set; }
    public double CpuStdDev { get; set; }
    public double CpuMin { get; set; }
    public double CpuMax { get; set; }
    public double MemoryAverage { get; set; }
    public double MemoryStdDev { get; set; }
    public double MemoryMin { get; set; }
    public double MemoryMax { get; set; }
    public double NetworkConnectionAverage { get; set; }
    public double NetworkConnectionStdDev { get; set; }
    public int LearningDurationMinutes { get; set; }
    public int SampleCount { get; set; }
    public int ExpectedSampleCount { get; set; }
    public double SampleCoveragePercent { get; set; }
    public double MaximumSampleGapSeconds { get; set; }
    public string Status { get; set; } = "Active";
}
