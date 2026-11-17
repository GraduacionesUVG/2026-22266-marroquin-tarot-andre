using BotnetMiningDetector.App.Agent;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Baseline;

// servicio de construccion de linea base
public sealed class BaselineService
{
    private readonly MonitoringService _monitoringService;
    private readonly SqliteRepository _repository;

    // crea el servicio de linea base
    public BaselineService(MonitoringService monitoringService, SqliteRepository repository)
    {
        _monitoringService = monitoringService;
        _repository = repository;
    }

    // recolecta muestras y calcula la linea base
    public async Task<BaselineBuildResult> BuildBaselineAsync(
        AgentSettings settings,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.Now;
        settings.Normalize();
        var duration = TimeSpan.FromMinutes(Math.Max(1, settings.BaselineLearningMinutes));
        var samplingInterval = TimeSpan.FromSeconds(Math.Max(1, settings.MonitoringIntervalSeconds));
        var finishedAt = startedAt.Add(duration);
        var systemMetrics = new List<SystemMetric>();
        var processMetrics = new List<ProcessMetric>();
        var sampleNumber = 0;
        DateTime? previousSampleAt = null;
        var maximumSampleGapSeconds = 0d;

        while (DateTime.Now < finishedAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await _monitoringService.CollectAndStoreAsync(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                AgentStatus.Aprendizaje,
                cancellationToken);

            systemMetrics.Add(snapshot.SystemMetric);
            processMetrics.AddRange(snapshot.ProcessMetrics);
            sampleNumber++;
            if (previousSampleAt is not null)
            {
                maximumSampleGapSeconds = Math.Max(
                    maximumSampleGapSeconds,
                    (snapshot.Timestamp - previousSampleAt.Value).TotalSeconds);
            }

            previousSampleAt = snapshot.Timestamp;

            var elapsed = DateTime.Now - startedAt;
            var remaining = finishedAt - DateTime.Now;
            progress?.Report(new BaselineProgress
            {
                SamplesCollected = sampleNumber,
                Elapsed = elapsed,
                Remaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
                Message = $"Recolectando línea base: {sampleNumber} muestras"
            });

            if (DateTime.Now.Add(samplingInterval) > finishedAt)
            {
                break;
            }

            await Task.Delay(samplingInterval, cancellationToken);
        }

        var expectedSampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / samplingInterval.TotalSeconds));
        var sampleCoveragePercent = Math.Min(100, sampleNumber * 100d / expectedSampleCount);
        var minimumSampleCount = Math.Max(
            2,
            (int)Math.Ceiling(expectedSampleCount * settings.BaselineMinimumSampleCoveragePercent / 100d));
        var maximumAllowedGapSeconds = Math.Max(30, samplingInterval.TotalSeconds * 6);

        if (sampleNumber < minimumSampleCount || maximumSampleGapSeconds > maximumAllowedGapSeconds)
        {
            throw new InvalidOperationException(
                $"La línea base no alcanzó la calidad mínima de muestreo. " +
                $"Muestras: {sampleNumber}/{expectedSampleCount} ({sampleCoveragePercent:N1}%). " +
                $"Brecha máxima: {maximumSampleGapSeconds:N1}s. La línea base anterior se conserva.");
        }

        var cpuValues = systemMetrics.Select(metric => metric.CpuTotalPercent).ToList();
        var memoryValues = systemMetrics.Select(metric => metric.MemoryUsedPercent).ToList();
        var networkTotals = processMetrics
            .GroupBy(metric => metric.Timestamp)
            .Select(group => (double)group.Sum(metric => metric.NetworkConnectionCount))
            .ToList();

        var profile = new BaselineProfile
        {
            CreatedAt = startedAt,
            UpdatedAt = DateTime.Now,
            CpuAverage = Math.Round(MathStatistics.Average(cpuValues), 2),
            CpuStdDev = Math.Round(MathStatistics.StandardDeviation(cpuValues), 2),
            CpuMin = Math.Round(cpuValues.DefaultIfEmpty(0).Min(), 2),
            CpuMax = Math.Round(cpuValues.DefaultIfEmpty(0).Max(), 2),
            MemoryAverage = Math.Round(MathStatistics.Average(memoryValues), 2),
            MemoryStdDev = Math.Round(MathStatistics.StandardDeviation(memoryValues), 2),
            MemoryMin = Math.Round(memoryValues.DefaultIfEmpty(0).Min(), 2),
            MemoryMax = Math.Round(memoryValues.DefaultIfEmpty(0).Max(), 2),
            NetworkConnectionAverage = Math.Round(MathStatistics.Average(networkTotals), 2),
            NetworkConnectionStdDev = Math.Round(MathStatistics.StandardDeviation(networkTotals), 2),
            LearningDurationMinutes = Math.Max(1, settings.BaselineLearningMinutes),
            SampleCount = sampleNumber,
            ExpectedSampleCount = expectedSampleCount,
            SampleCoveragePercent = Math.Round(sampleCoveragePercent, 2),
            MaximumSampleGapSeconds = Math.Round(maximumSampleGapSeconds, 2),
            Status = "Active"
        };

        var occurrenceThreshold = Math.Max(1, (int)Math.Ceiling(Math.Max(1, sampleNumber) * 0.30));
        var processBaselines = processMetrics
            .GroupBy(metric => metric.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProcessBaseline
            {
                ProcessName = group.Key,
                AverageCpu = Math.Round(MathStatistics.Average(group.Select(metric => metric.CpuPercent)), 2),
                StdDevCpu = Math.Round(MathStatistics.StandardDeviation(group.Select(metric => metric.CpuPercent)), 2),
                AverageMemory = Math.Round(MathStatistics.Average(group.Select(metric => metric.MemoryMb)), 2),
                StdDevMemory = Math.Round(MathStatistics.StandardDeviation(group.Select(metric => metric.MemoryMb)), 2),
                AverageNetworkConnections = Math.Round(MathStatistics.Average(group.Select(metric => (double)metric.NetworkConnectionCount)), 2),
                StdDevNetworkConnections = Math.Round(MathStatistics.StandardDeviation(group.Select(metric => (double)metric.NetworkConnectionCount)), 2),
                TypicalLifetimeSeconds = Math.Round(MathStatistics.Average(group.Select(metric => metric.LifetimeSeconds ?? 0)), 2),
                OccurrenceCount = group.Count()
            })
            .Where(process => process.OccurrenceCount >= occurrenceThreshold)
            .OrderByDescending(process => process.OccurrenceCount)
            .ThenBy(process => process.ProcessName)
            .ToList();

        await _repository.ReplaceBaselineAsync(profile, processBaselines, cancellationToken);
        progress?.Report(new BaselineProgress
        {
            SamplesCollected = sampleNumber,
            Elapsed = DateTime.Now - startedAt,
            Remaining = TimeSpan.Zero,
            Message = "Línea base construida correctamente"
        });

        return new BaselineBuildResult
        {
            Profile = profile,
            ProcessBaselines = processBaselines
        };
    }
}
