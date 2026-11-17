namespace BotnetMiningDetector.App.Data.Models;

// medicion global del sistema
public sealed class SystemMetric
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double CpuTotalPercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double MemoryUsedPercent { get; set; }
    public string MonitoringStatus { get; set; } = "No disponible";
}
