using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Evaluation;

// calculador de metricas de evaluacion
public sealed class EvaluationMetricsCalculator
{
    // calcula metricas sin alertas detalladas
    public EvaluationSummary Calculate(IEnumerable<ValidationScenario> scenarios, IEnumerable<ProcessMetric> processMetrics)
    {
        return Calculate(scenarios, processMetrics, Enumerable.Empty<Alert>());
    }

    // calcula metricas con evidencia de alertas
    public EvaluationSummary Calculate(IEnumerable<ValidationScenario> scenarios, IEnumerable<ProcessMetric> processMetrics, IEnumerable<Alert> alerts)
    {
        var completedScenarios = scenarios.Where(IsEvaluatedScenario).ToList();
        var truePositives = completedScenarios.Count(scenario => scenario.ConfusionResult.Equals("TP", StringComparison.OrdinalIgnoreCase));
        var falsePositives = completedScenarios.Count(scenario => scenario.ConfusionResult.Equals("FP", StringComparison.OrdinalIgnoreCase));
        var falseNegatives = completedScenarios.Count(scenario => scenario.ConfusionResult.Equals("FN", StringComparison.OrdinalIgnoreCase));
        var trueNegatives = completedScenarios.Count(scenario => scenario.ConfusionResult.Equals("TN", StringComparison.OrdinalIgnoreCase));

        var agentMetrics = processMetrics
            .Where(metric => metric.ProcessName.Contains("BotnetMiningDetector.App", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var processList = processMetrics.ToList();
        var distinctProcessNames = processList
            .Select(metric => metric.ProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var coveredProcessNames = processList
            .Where(metric => metric.IsKnownProcess)
            .Select(metric => metric.ProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var alertList = alerts.ToList();

        return new EvaluationSummary
        {
            Timestamp = DateTime.Now,
            TruePositives = truePositives,
            FalsePositives = falsePositives,
            FalseNegatives = falseNegatives,
            TrueNegatives = trueNegatives,
            Precision = SafeDivide(truePositives, truePositives + falsePositives),
            Recall = SafeDivide(truePositives, truePositives + falseNegatives),
            FalsePositiveRate = SafeDivide(falsePositives, falsePositives + trueNegatives),
            AverageDetectionTimeSeconds = Math.Round(MathStatistics.Average(completedScenarios.Where(scenario => scenario.DetectionTimeSeconds is not null).Select(scenario => scenario.DetectionTimeSeconds!.Value)), 2),
            AgentAverageCpuPercent = Math.Round(MathStatistics.Average(agentMetrics.Select(metric => metric.CpuPercent)), 2),
            AgentAverageMemoryMb = Math.Round(MathStatistics.Average(agentMetrics.Select(metric => metric.MemoryMb)), 2),
            AgentMaximumMemoryMb = Math.Round(agentMetrics.Select(metric => metric.MemoryMb).DefaultIfEmpty(0).Max(), 2),
            BaselineCoverage = SafeDivide(coveredProcessNames, distinctProcessNames.Count),
            AlertClarityScore = CalculateAlertClarity(alertList),
            StatisticalActivationCount = alertList.Count(alert => alert.ThresholdType.Equals("Statistical", StringComparison.OrdinalIgnoreCase)),
            HeuristicActivationCount = alertList.Count(alert => alert.ThresholdType.Equals("Heuristic", StringComparison.OrdinalIgnoreCase)),
            CorrelationActivationCount = alertList.Count(alert => alert.ThresholdType.Equals("Correlation", StringComparison.OrdinalIgnoreCase)),
            AbsoluteActivationCount = alertList.Count(alert => alert.ThresholdType.Equals("Absolute", StringComparison.OrdinalIgnoreCase))
        };
    }

    // valida si un escenario cuenta en las metricas
    private static bool IsEvaluatedScenario(ValidationScenario scenario)
    {
        return scenario.ConfusionResult.Equals("TP", StringComparison.OrdinalIgnoreCase) ||
            scenario.ConfusionResult.Equals("FP", StringComparison.OrdinalIgnoreCase) ||
            scenario.ConfusionResult.Equals("FN", StringComparison.OrdinalIgnoreCase) ||
            scenario.ConfusionResult.Equals("TN", StringComparison.OrdinalIgnoreCase);
    }

    // divide evitando resultados invalidos
    private static double SafeDivide(double numerator, double denominator)
    {
        return denominator == 0 ? 0 : Math.Round(numerator / denominator, 4);
    }

    // calcula claridad de las alertas
    private static double CalculateAlertClarity(IReadOnlyCollection<Alert> alerts)
    {
        if (alerts.Count == 0)
        {
            return 0;
        }

        var clearAlerts = alerts.Count(alert =>
            !string.IsNullOrWhiteSpace(alert.ProcessName) &&
            !string.IsNullOrWhiteSpace(alert.MetricObserved) &&
            !string.IsNullOrWhiteSpace(alert.RuleName) &&
            !string.IsNullOrWhiteSpace(alert.Description) &&
            !string.IsNullOrWhiteSpace(alert.Recommendation) &&
            !string.IsNullOrWhiteSpace(alert.EvidenceDetails));

        return SafeDivide(clearAlerts, alerts.Count);
    }
}
