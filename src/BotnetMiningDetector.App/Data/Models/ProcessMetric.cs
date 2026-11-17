namespace BotnetMiningDetector.App.Data.Models;

// medicion de un proceso
public sealed class ProcessMetric
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = "No disponible";
    public double CpuPercent { get; set; }
    public double MemoryMb { get; set; }
    public DateTime? StartTime { get; set; }
    public double? LifetimeSeconds { get; set; }
    public int NetworkConnectionCount { get; set; }
    public bool IsKnownProcess { get; set; }
}
