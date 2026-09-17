namespace Flyback.App.Controls;

/// <summary>
/// A knob's value as a number box can hold it.
/// </summary>
/// <remarks>
/// A knob is a <see cref="float"/> and a <c>NumericUpDown</c> holds a
/// <see cref="decimal"/>, which reaches only to about 7.9e28 and throws past it. A
/// file or a line of text can say any float, so the box shows the nearest number
/// it has and the patch keeps the one it was given.
/// </remarks>
internal static class Boxed
{
    /// <summary>Inside <see cref="decimal.MaxValue"/> by more than a float's rounding.</summary>
    private const float Furthest = 7.9e28f;

    public static decimal Of(float value) =>
        float.IsNaN(value) ? 0m : (decimal)Math.Clamp(value, -Furthest, Furthest);
}
