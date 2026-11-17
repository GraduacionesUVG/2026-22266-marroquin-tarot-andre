using System.Diagnostics;

namespace BotnetMiningDetector.App.Agent;

// lector de conexiones tcp
public sealed class NetworkConnectionReader
{
    // cuenta conexiones tcp por proceso
    public async Task<Dictionary<int, int>> ReadTcpConnectionCountsByPidAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new Dictionary<int, int>();
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ParseNetstatOutput(output);
        }
        catch
        {
            return new Dictionary<int, int>();
        }
    }

    // procesa la salida de netstat
    private static Dictionary<int, int> ParseNetstatOutput(string output)
    {
        var counts = new Dictionary<int, int>();
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !int.TryParse(parts[^1], out var pid))
            {
                continue;
            }

            counts[pid] = counts.TryGetValue(pid, out var current) ? current + 1 : 1;
        }

        return counts;
    }
}
