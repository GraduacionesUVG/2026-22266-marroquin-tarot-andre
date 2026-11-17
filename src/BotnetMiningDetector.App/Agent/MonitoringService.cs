using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data;

namespace BotnetMiningDetector.App.Agent;

// servicio principal de monitoreo
public sealed class MonitoringService
{
    private readonly ProcessSampler _processSampler;
    private readonly SqliteRepository _repository;

    // crea el servicio de monitoreo
    public MonitoringService(ProcessSampler processSampler, SqliteRepository repository)
    {
        _processSampler = processSampler;
        _repository = repository;
    }

    // recolecta y guarda la telemetria
    public async Task<TelemetrySnapshot> CollectAndStoreAsync(IReadOnlySet<string> knownProcessNames, AgentStatus status, CancellationToken cancellationToken = default)
    {
        var snapshot = await _processSampler.CollectAsync(knownProcessNames, status, cancellationToken);
        await _repository.InsertSystemMetricAsync(snapshot.SystemMetric, cancellationToken);
        await _repository.InsertProcessMetricsAsync(snapshot.ProcessMetrics, cancellationToken);
        return snapshot;
    }
}
