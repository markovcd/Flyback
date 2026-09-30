using System.Globalization;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Language;

/// <summary>Writes individual values and carried notes, scales and parts in language syntax.</summary>
internal static class PatchValueWriter
{
    internal static string? Carried(NodeInstance node, NodeDef def)
    {
        if (def.Extra<StepsExtra>() is { } steps)
        {
            var written = StepsExtra.Of(node);

            if (written.Count == 0) return null;

            var note = steps.Spec.Display == PortDisplay.Note;

            return "[ " + string.Join(' ', written.Select(step => Step(step, note))) + " ]";
        }

        if (def.Extra<ScaleExtra>() is not null)
        {
            var scale = ScaleExtra.Of(node);

            return scale.Count == 0 ? null : "[ " + string.Join(' ', scale.Select(Pitch.ClassName)) + " ]";
        }

        if (def.Extra<ArrangementExtra>() is not null) return ArrangementNotation.Write(ArrangementExtra.Of(node));

        return null;
    }

    private static string Step(Step step, bool note)
    {
        // A rest has no pitch to write. A note at no volume keeps its own,
        // and the two are different steps however alike they sound.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        var head = step.Volume <= 0f && step.Value == 0f
            ? "~"
            : note ? Pitch.Name(step.Value) : Number(step.Value);

        if (step.Volume < 1f && head != "~") head += "%" + Number(step.Volume);
        if (Math.Abs(step.Length - 1f) > 1e-6f) head += "@" + Number(step.Length);

        return head;
    }

    internal static string Value(float value, PortDisplay display) => display switch
    {
        PortDisplay.Note => Whole(value) ? Pitch.Name(value) : Number(value),
        PortDisplay.Duration => Seconds(value),
        PortDisplay.Integer or PortDisplay.Chord when value == MathF.Round(value) => value.ToString("0", CultureInfo.InvariantCulture),
        _ => Number(value),
    };

    internal static string Number(float value)
    {
        if (!float.IsFinite(value)) return "0";

        var exact = (double)value;

        // Enough places for the smallest float: 45 zeros, then its nine digits.
        for (var digits = 0; digits <= 54; digits++)
        {
            var written = exact.ToString(
                digits == 0 ? "0" : "0." + new string('#', digits), CultureInfo.InvariantCulture);

            if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var back)
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                && (float)back == value)
            {
                return written;
            }
        }

        return exact.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static bool Whole(float value) => Math.Abs(value - MathF.Round(value)) < 1e-6f;

    /// <summary>
    /// A Duration knob written as the time it means, where saying it that way
    /// reads back as the same number.
    /// </summary>
    /// <remarks>
    /// The socket holds a power of ten, and the whole point of the literal is
    /// that nobody should have to. But not every decade is a round time, so the
    /// time is written and then checked: if reading it back does not land on the
    /// same knob, the decade is written instead and is exactly right.
    /// </remarks>
    private static string Seconds(float decades)
    {
        if (!float.IsFinite(decades)) return Number(decades);

        var seconds = Math.Pow(10d, decades);

        var (unit, name) = seconds switch
        {
            < 1e-3d => (1e-6d, "us"),
            < 1d => (1e-3d, "ms"),
            _ => (1d, "s"),
        };

        for (var digits = 0; digits <= 17; digits++)
        {
            var written = (seconds / unit).ToString(
                digits == 0 ? "0" : "0." + new string('#', digits), CultureInfo.InvariantCulture);

            if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var back)) continue;

            // Match the lexer and binder's conversion exactly; approximate
            // round-tripping would make repeated edits drift the knob.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if ((float)Math.Log10(back * unit) == decades) return written + name;
        }

        return (seconds / unit).ToString("0.#################", CultureInfo.InvariantCulture) + name;
    }
}
