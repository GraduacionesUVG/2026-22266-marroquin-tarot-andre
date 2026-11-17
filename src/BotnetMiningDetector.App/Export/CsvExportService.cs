using System.Globalization;
using System.IO;
using System.Text;

using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;
using BotnetMiningDetector.App.Data.Models;
using BotnetMiningDetector.App.Evaluation;

namespace BotnetMiningDetector.App.Export;

// servicio de exportacion local
public sealed class CsvExportService
{
    private readonly SqliteRepository _repository;
    private readonly EvaluationMetricsCalculator _metricsCalculator;

    // crea el servicio de exportacion
    public CsvExportService(SqliteRepository repository, EvaluationMetricsCalculator metricsCalculator)
    {
        _repository = repository;
        _metricsCalculator = metricsCalculator;
    }

    // exporta los resultados de la aplicacion
    public async Task<string> ExportAllAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Normalize();
        var exportRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var exportDirectory = Path.Combine(exportRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(exportDirectory);

        var systemMetrics = await _repository.GetSystemMetricsAsync(cancellationToken);
        var processMetrics = await _repository.GetProcessMetricsAsync(cancellationToken);
        var alerts = await _repository.GetRecentAlertsAsync(int.MaxValue, cancellationToken);
        var scenarios = await _repository.GetValidationScenariosAsync(cancellationToken);
        var summary = _metricsCalculator.Calculate(scenarios, processMetrics, alerts);
        await _repository.SaveEvaluationSummaryAsync(summary, cancellationToken);

        await WriteSystemMetricsAsync(Path.Combine(exportDirectory, "metricas_sistema.csv"), systemMetrics, cancellationToken);
        await WriteProcessMetricsAsync(Path.Combine(exportDirectory, "metricas_procesos.csv"), processMetrics, cancellationToken);
        await WriteAlertsAsync(Path.Combine(exportDirectory, "alertas.csv"), alerts, cancellationToken);
        await WriteValidationScenariosAsync(Path.Combine(exportDirectory, "escenarios_validacion.csv"), scenarios, cancellationToken);
        await WriteEvaluationSummaryAsync(Path.Combine(exportDirectory, "resumen_evaluacion.csv"), summary, cancellationToken);

        return exportDirectory;
    }

    // escribe metricas globales en csv
    private static async Task WriteSystemMetricsAsync(string path, IEnumerable<SystemMetric> metrics, CancellationToken cancellationToken)
    {
        var lines = new List<string> { "Id,Timestamp,CpuTotalPercent,MemoryUsedMb,MemoryTotalMb,MemoryUsedPercent,MonitoringStatus" };
        lines.AddRange(metrics.Select(metric => string.Join(',',
            metric.Id,
            Escape(metric.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            Format(metric.CpuTotalPercent),
            Format(metric.MemoryUsedMb),
            Format(metric.MemoryTotalMb),
            Format(metric.MemoryUsedPercent),
            Escape(metric.MonitoringStatus))));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
    }

    // escribe metricas de procesos en csv
    private static async Task WriteProcessMetricsAsync(string path, IEnumerable<ProcessMetric> metrics, CancellationToken cancellationToken)
    {
        var lines = new List<string> { "Id,Timestamp,ProcessId,ProcessName,ExecutablePath,CpuPercent,MemoryMb,StartTime,LifetimeSeconds,NetworkConnectionCount,IsKnownProcess" };
        lines.AddRange(metrics.Select(metric => string.Join(',',
            metric.Id,
            Escape(metric.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            metric.ProcessId,
            Escape(metric.ProcessName),
            Escape(metric.ExecutablePath),
            Format(metric.CpuPercent),
            Format(metric.MemoryMb),
            Escape(metric.StartTime?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
            Format(metric.LifetimeSeconds),
            metric.NetworkConnectionCount,
            metric.IsKnownProcess ? "1" : "0")));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
    }

    // escribe alertas en csv
    private static async Task WriteAlertsAsync(string path, IEnumerable<Alert> alerts, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "Id,Timestamp,Severity,AlertType,ProcessId,ProcessName,MetricObserved,ObservedValue,BaselineAverage,BaselineStdDev,StdDevMultiplier,CalculatedThreshold,AbsoluteThreshold,ThresholdType,SustainedDurationSeconds,RuleName,RuleCategory,BaselineReference,Description,Recommendation,EvidenceDetails,WasAcknowledged"
        };
        lines.AddRange(alerts.Select(alert => string.Join(',',
            alert.Id,
            Escape(alert.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            Escape(alert.Severity),
            Escape(alert.AlertType),
            alert.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(alert.ProcessName),
            Escape(alert.MetricObserved),
            Format(alert.ObservedValue),
            Format(alert.BaselineAverage),
            Format(alert.BaselineStdDev),
            Format(alert.StdDevMultiplier),
            Format(alert.CalculatedThreshold),
            Format(alert.AbsoluteThreshold),
            Escape(alert.ThresholdType),
            Format(alert.SustainedDurationSeconds),
            Escape(alert.RuleName),
            Escape(alert.RuleCategory),
            Escape(alert.BaselineReference),
            Escape(alert.Description),
            Escape(alert.Recommendation),
            Escape(alert.EvidenceDetails),
            alert.WasAcknowledged ? "1" : "0")));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
    }

    // escribe escenarios en csv
    private static async Task WriteValidationScenariosAsync(string path, IEnumerable<ValidationScenario> scenarios, CancellationToken cancellationToken)
    {
        var lines = new List<string> { "Id,Name,ScenarioType,ScenarioCategory,Description,ProcessId,ProcessName,StartedAt,FinishedAt,DurationSeconds,ExpectedResult,ActualResult,ExpectedClassification,ActualClassification,ConfusionResult,AlertGenerated,DetectionTimeSeconds,TelemetrySampleCount,AverageCpuPercent,MaximumCpuPercent,AverageMemoryMb,MaximumMemoryMb,AverageNetworkConnections,MaximumNetworkConnections,TriggeredRuleName,ObservedActivity,Notes" };
        lines.AddRange(scenarios.Select(scenario => string.Join(',',
            scenario.Id,
            Escape(scenario.Name),
            Escape(scenario.ScenarioType),
            Escape(scenario.ScenarioCategory),
            Escape(scenario.Description),
            scenario.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(scenario.ProcessName),
            Escape(scenario.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
            Escape(scenario.FinishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
            Format(scenario.DurationSeconds),
            Escape(scenario.ExpectedResult),
            Escape(scenario.ActualResult),
            Escape(scenario.ExpectedClassification),
            Escape(scenario.ActualClassification),
            Escape(scenario.ConfusionResult),
            scenario.AlertGenerated ? "1" : "0",
            Format(scenario.DetectionTimeSeconds),
            scenario.TelemetrySampleCount,
            Format(scenario.AverageCpuPercent),
            Format(scenario.MaximumCpuPercent),
            Format(scenario.AverageMemoryMb),
            Format(scenario.MaximumMemoryMb),
            Format(scenario.AverageNetworkConnections),
            scenario.MaximumNetworkConnections?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(scenario.TriggeredRuleName),
            Escape(scenario.ObservedActivity),
            Escape(scenario.Notes))));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
    }

    // escribe el resumen de evaluacion
    private static async Task WriteEvaluationSummaryAsync(string path, EvaluationSummary summary, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "Timestamp,TruePositives,FalsePositives,FalseNegatives,TrueNegatives,Precision,Recall,FalsePositiveRate,AverageDetectionTimeSeconds,AgentAverageCpuPercent,AgentAverageMemoryMb,AgentMaximumMemoryMb,BaselineCoverage,AlertClarityScore,StatisticalActivationCount,HeuristicActivationCount,CorrelationActivationCount,AbsoluteActivationCount",
            string.Join(',',
                Escape(summary.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                summary.TruePositives,
                summary.FalsePositives,
                summary.FalseNegatives,
                summary.TrueNegatives,
                Format(summary.Precision),
                Format(summary.Recall),
                Format(summary.FalsePositiveRate),
                Format(summary.AverageDetectionTimeSeconds),
                Format(summary.AgentAverageCpuPercent),
                Format(summary.AgentAverageMemoryMb),
                Format(summary.AgentMaximumMemoryMb),
                Format(summary.BaselineCoverage),
                Format(summary.AlertClarityScore),
                summary.StatisticalActivationCount,
                summary.HeuristicActivationCount,
                summary.CorrelationActivationCount,
                summary.AbsoluteActivationCount)
        };
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken);
    }

    // prepara valores numericos para csv
    private static string Format(double? value)
    {
        return value is null ? string.Empty : value.Value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    // escapa valores de texto para csv
    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
