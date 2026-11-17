using BotnetMiningDetector.App.Data.Models;

namespace BotnetMiningDetector.App.Baseline;

// resultado de la construccion de linea base
public sealed class BaselineBuildResult
{
    public BaselineProfile Profile { get; set; } = new();
    public List<ProcessBaseline> ProcessBaselines { get; set; } = new();
}
