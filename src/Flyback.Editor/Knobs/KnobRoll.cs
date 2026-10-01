namespace Flyback.App.Knobs;

/// <summary>Where a randomized knob goes.</summary>
internal static class KnobRoll
{
    /// <summary>
    /// Anywhere within <paramref name="amount"/> of <paramref name="at"/> on the
    /// knob, evenly; an amount of 1 is anywhere on it.
    /// </summary>
    public static float Next(float at, double amount, Random random)
    {
        var reach = (float)Math.Clamp(amount, 0, 1);
        var low = Math.Max(0f, at - reach);
        var high = Math.Min(1f, at + reach);

        return low + (high - low) * random.NextSingle();
    }
}
