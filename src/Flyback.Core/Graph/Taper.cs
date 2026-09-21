namespace Flyback.Core.Graph;

/// <summary>
/// How a control's travel, 0 to 1, maps onto a range: evenly for a knee of 0,
/// otherwise evenly below the knee and in decades above it. A range whose
/// <c>max</c> is under its <c>min</c> is swept the other way round.
/// </summary>
internal static class Taper
{
    /// <summary>The value <paramref name="travel"/> of the way from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static float At(double travel, float min, float max, float knee)
    {
        travel = Math.Clamp(travel, 0, 1);

        if (knee <= 0f) return (float)(min + (max - (double)min) * travel);
        if (max < min) return At(1 - travel, max, min, knee);

        return (float)(min + knee * (Math.Pow(1 + (max - (double)min) / knee, travel) - 1));
    }

    /// <summary>How far from <paramref name="min"/> to <paramref name="max"/> <paramref name="value"/> sits, 0 to 1.</summary>
    public static double Travel(float value, float min, float max, float knee)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (max == min) return 0.5;
        if (max < min) return 1 - Travel(value, max, min, knee);

        var at = Math.Clamp(value, min, max) - (double)min;

        return knee > 0f
            ? Math.Log(1 + at / knee) / Math.Log(1 + (max - (double)min) / knee)
            : at / (max - (double)min);
    }
}
