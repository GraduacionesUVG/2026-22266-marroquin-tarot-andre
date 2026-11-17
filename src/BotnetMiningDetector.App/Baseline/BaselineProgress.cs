namespace BotnetMiningDetector.App.Baseline;

// progreso de aprendizaje de la linea base
public sealed class BaselineProgress
{
    public int SamplesCollected { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Remaining { get; set; }
    public string Message { get; set; } = string.Empty;
}
