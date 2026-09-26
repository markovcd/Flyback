namespace Flyback.Core.Graph;

/// <summary>
/// The range an Auto remap's pair of knobs are fractions of, swept the way the
/// socket at the far end of the wire sweeps its own knob.
/// </summary>
internal readonly record struct RemapSpan(float Min, float Max, float Knee = 0f, PortDisplay Display = PortDisplay.Number)
{
    /// <summary>What an unwired side is: fractions of 0..1, which are the numbers themselves.</summary>
    public static RemapSpan Unit => new(0f, 1f);

    /// <summary>
    /// The value <paramref name="travel"/> of the way along, unclamped so a knob
    /// past 1 carries on the way the compiled module does.
    /// </summary>
    public float At(float travel)
    {
        if (Knee <= 0f) return Min + (Max - Min) * travel;

        var low = MathF.Min(Min, Max);
        var span = MathF.Abs(Max - Min);
        var along = Max < Min ? 1f - travel : travel;

        return low + Knee * (MathF.Exp(along * MathF.Log(1f + span / Knee)) - 1f);
    }

    /// <summary>The value <paramref name="travel"/> of the way along, written the way the socket writes its own.</summary>
    public string Format(float travel) => new PortSpec("", Min: Min, Max: Max, Display: Display).Format(At(travel));

    /// <summary>How far along <paramref name="value"/> sits, 0 to 1 inside the range, the inverse of <see cref="At"/>.</summary>
    public float Travel(float value)
    {
        if (Knee <= 0f) return (value - Min) / (Max - Min);

        var low = MathF.Min(Min, Max);
        var up = MathF.Log(1f + (value - low) / Knee) / MathF.Log(1f + MathF.Abs(Max - Min) / Knee);

        return Max < Min ? 1f - up : up;
    }
}