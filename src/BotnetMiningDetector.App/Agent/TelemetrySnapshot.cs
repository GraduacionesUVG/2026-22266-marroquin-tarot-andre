using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Agent;

// muestra completa de telemetria
public sealed class TelemetrySnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public SystemMetric SystemMetric { get; set; } = new();
    public List<ProcessMetric> ProcessMetrics { get; set; } = new();
    public string StatusMessage { get; set; } = "Correcto";
}
