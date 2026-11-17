namespace BotnetMiningDetector.App.Common;

// calculos estadisticos del agente
public static class MathStatistics
{
    // calcula el promedio de una serie
    public static double Average(IEnumerable<double> values)
    {
        var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    // calcula la desviacion estandar
    public static double StandardDeviation(IEnumerable<double> values)
    {
        var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (list.Count <= 1)
        {
            return 0;
        }

        var average = list.Average();
        var variance = list.Sum(value => Math.Pow(value - average, 2)) / (list.Count - 1);
        return Math.Sqrt(variance);
    }
}
