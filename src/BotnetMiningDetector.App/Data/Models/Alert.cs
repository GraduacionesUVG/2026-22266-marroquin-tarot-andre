namespace BotnetMiningDetector.App.Data.Models;

// modelo de alerta del agente
public sealed class Alert
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Severity { get; set; } = "Media";
    public string AlertType { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string MetricObserved { get; set; } = string.Empty;
    public string BaselineReference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public double? ObservedValue { get; set; }
    public double? BaselineAverage { get; set; }
    public double? BaselineStdDev { get; set; }
    public double? StdDevMultiplier { get; set; }
    public double? CalculatedThreshold { get; set; }
    public double? AbsoluteThreshold { get; set; }
    public string ThresholdType { get; set; } = "Heuristic";
    public double? SustainedDurationSeconds { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string RuleCategory { get; set; } = string.Empty;
    public string EvidenceDetails { get; set; } = string.Empty;
    public bool WasAcknowledged { get; set; }
}
