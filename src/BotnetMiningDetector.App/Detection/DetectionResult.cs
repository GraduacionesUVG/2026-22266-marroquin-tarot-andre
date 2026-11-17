using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Detection;

// resultado del analisis de deteccion
public sealed class DetectionResult
{
    public List<Alert> Alerts { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}
