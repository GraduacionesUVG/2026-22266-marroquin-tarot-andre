using System.Diagnostics;
using BotnetMiningDetector.App.Common;
using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Agent;

// recolector de procesos del sistema
public sealed class ProcessSampler
{
    private readonly SystemResourceReader _systemResourceReader;
    private readonly NetworkConnectionReader _networkConnectionReader;
    private readonly Dictionary<int, ProcessCpuState> _previousCpuStates = new();
    private DateTime? _previousSampleAtUtc;

    // crea el recolector de procesos
    public ProcessSampler(SystemResourceReader systemResourceReader, NetworkConnectionReader networkConnectionReader)
    {
        _systemResourceReader = systemResourceReader;
        _networkConnectionReader = networkConnectionReader;
    }

    // recolecta metricas de procesos y del sistema
    public async Task<TelemetrySnapshot> CollectAsync(IReadOnlySet<string> knownProcessNames, AgentStatus status, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.Now;
        var statusText = status switch
        {
            AgentStatus.Activo => "Activo",
            AgentStatus.Aprendizaje => "Aprendizaje",
            _ => "Pausado"
        };

        var systemMetric = _systemResourceReader.ReadSystemMetric(timestamp, statusText);
        var connectionsByPid = await _networkConnectionReader.ReadTcpConnectionCountsByPidAsync(cancellationToken);
        var processMetrics = new List<ProcessMetric>();
        var currentSampleAtUtc = DateTime.UtcNow;
        var elapsedMs = _previousSampleAtUtc is null ? 0 : (currentSampleAtUtc - _previousSampleAtUtc.Value).TotalMilliseconds;
        var seenPids = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var pid = process.Id;
                seenPids.Add(pid);

                var totalProcessorTime = TryRead(() => process.TotalProcessorTime, TimeSpan.Zero);
                var cpuPercent = CalculateProcessCpu(pid, totalProcessorTime, elapsedMs);
                var startTime = TryRead<DateTime?>(() => process.StartTime, null);
                var executablePath = TryRead(() => process.MainModule?.FileName ?? "No disponible", "No disponible");
                double? lifetimeSeconds = startTime is null ? null : Math.Max(0, (timestamp - startTime.Value).TotalSeconds);
                var processName = TryRead(() => process.ProcessName, "No disponible");

                processMetrics.Add(new ProcessMetric
                {
                    Timestamp = timestamp,
                    ProcessId = pid,
                    ProcessName = processName,
                    ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? "No disponible" : executablePath,
                    CpuPercent = cpuPercent,
                    MemoryMb = Math.Round(Math.Max(0, TryRead(() => process.WorkingSet64, 0L)) / 1024d / 1024d, 2),
                    StartTime = startTime,
                    LifetimeSeconds = lifetimeSeconds is null ? null : Math.Round(lifetimeSeconds.Value, 2),
                    NetworkConnectionCount = connectionsByPid.TryGetValue(pid, out var count) ? count : 0,
                    IsKnownProcess = knownProcessNames.Contains(processName)
                });
            }
            catch
            {
                // algunos procesos no exponen todas sus propiedades
            }
            finally
            {
                process.Dispose();
            }
        }

        RemoveExitedProcesses(seenPids);
        _previousSampleAtUtc = currentSampleAtUtc;

        return new TelemetrySnapshot
        {
            Timestamp = timestamp,
            SystemMetric = systemMetric,
            ProcessMetrics = processMetrics.OrderByDescending(process => process.CpuPercent).ThenByDescending(process => process.MemoryMb).ToList(),
            StatusMessage = "Correcto"
        };
    }

    // calcula el uso de cpu por proceso
    private double CalculateProcessCpu(int pid, TimeSpan totalProcessorTime, double elapsedMs)
    {
        var previous = _previousCpuStates.TryGetValue(pid, out var state) ? state : null;
        _previousCpuStates[pid] = new ProcessCpuState(totalProcessorTime);

        if (previous is null || elapsedMs <= 0)
        {
            return 0;
        }

        var cpuMs = (totalProcessorTime - previous.TotalProcessorTime).TotalMilliseconds;
        var cpuPercent = cpuMs / elapsedMs / Environment.ProcessorCount * 100;
        return Math.Round(Math.Clamp(cpuPercent, 0, 100), 2);
    }

    // elimina procesos que ya no estan activos
    private void RemoveExitedProcesses(HashSet<int> seenPids)
    {
        var exited = _previousCpuStates.Keys.Where(pid => !seenPids.Contains(pid)).ToList();
        foreach (var pid in exited)
        {
            _previousCpuStates.Remove(pid);
        }
    }

    // lee una propiedad sin interrumpir el monitoreo
    private static T TryRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record ProcessCpuState(TimeSpan TotalProcessorTime);
}
