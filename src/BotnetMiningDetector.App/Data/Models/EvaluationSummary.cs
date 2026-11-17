namespace BotnetMiningDetector.App.Data.Models;

// resumen de metricas de evaluacion
public sealed class EvaluationSummary
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int TruePositives { get; set; }
    public int FalsePositives { get; set; }
    public int FalseNegatives { get; set; }
    public int TrueNegatives { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double FalsePositiveRate { get; set; }
    public double AverageDetectionTimeSeconds { get; set; }
    public double AgentAverageCpuPercent { get; set; }
    public double AgentAverageMemoryMb { get; set; }
    public double AgentMaximumMemoryMb { get; set; }
    public double BaselineCoverage { get; set; }
    public double AlertClarityScore { get; set; }
    public int StatisticalActivationCount { get; set; }
    public int HeuristicActivationCount { get; set; }
    public int CorrelationActivationCount { get; set; }
    public int AbsoluteActivationCount { get; set; }
}
