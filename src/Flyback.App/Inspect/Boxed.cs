using Avalonia.Controls;

namespace Flyback.App.Inspect;

/// <summary>
/// What stands between a knob and the number box showing it: the knob's value as
/// a box can hold it, and a box that goes on saying it.
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

    /// <summary>
    /// Has a box say the number in force once it is left, where it was left empty.
    /// </summary>
    /// <remarks>
    /// An emptied box is no number, so whatever reads it keeps the one it had —
    /// and the box would go on showing nothing beside a knob that is somewhere.
    /// While it has the focus it is left alone: empty is what a box is on the way
    /// from one number to another.
    /// </remarks>
    /// <returns>The same box, so this can wrap the expression that makes one.</returns>
    public static NumericUpDown NeverBlank(NumericUpDown box)
    {
        var kept = box.Value;

        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } said) kept = said;
        };

        box.LostFocus += (_, _) =>
        {
            if (box.Value is null) box.Value = kept;
        };

        return box;
    }
}
