namespace BotnetMiningDetector.App.Data.Models;

// registro de un escenario de validacion
public sealed class ValidationScenario
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ScenarioType { get; set; } = string.Empty;
    public string ScenarioCategory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public string ExpectedResult { get; set; } = string.Empty;
    public string ActualResult { get; set; } = string.Empty;
    public string ExpectedClassification { get; set; } = string.Empty;
    public string ActualClassification { get; set; } = string.Empty;
    public string ConfusionResult { get; set; } = string.Empty;
    public bool AlertGenerated { get; set; }
    public double? DetectionTimeSeconds { get; set; }
    public int TelemetrySampleCount { get; set; }
    public double? AverageCpuPercent { get; set; }
    public double? MaximumCpuPercent { get; set; }
    public double? AverageMemoryMb { get; set; }
    public double? MaximumMemoryMb { get; set; }
    public double? AverageNetworkConnections { get; set; }
    public int? MaximumNetworkConnections { get; set; }
    public string TriggeredRuleName { get; set; } = string.Empty;
    public string ObservedActivity { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
