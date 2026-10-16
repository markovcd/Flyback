using Flyback.Core.Graph;

namespace Flyback.Editor.Knobs;

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

    /// <summary>
    /// Where <paramref name="control"/> goes: a knob anywhere within the amount, and a
    /// switch flipped with the amount as its chance, so an amount of 1 always flips it.
    /// </summary>
    public static float Next(PatchControl control, double amount, Random random) =>
        control.Kind == ControlKind.Knob ? Next(control.Value, amount, random)
        : random.NextDouble() < amount ? 1f - control.Value
        : control.Value;
}
