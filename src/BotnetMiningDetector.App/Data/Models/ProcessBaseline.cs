namespace BotnetMiningDetector.App.Data.Models;

// perfil base de un proceso
public sealed class ProcessBaseline
{
    public long Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double AverageCpu { get; set; }
    public double StdDevCpu { get; set; }
    public double AverageMemory { get; set; }
    public double StdDevMemory { get; set; }
    public double AverageNetworkConnections { get; set; }
    public double StdDevNetworkConnections { get; set; }
    public double TypicalLifetimeSeconds { get; set; }
    public int OccurrenceCount { get; set; }
}
