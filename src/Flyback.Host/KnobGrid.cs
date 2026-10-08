namespace Flyback.Host;

/// <summary>
/// Whether the panel knobs stand in a fixed grid rather than wrapping to the width
/// they are given, so each keeps the row and column of a controller's knob.
/// </summary>
/// <remarks>
/// Knobs fill a row before the next, and past the last row a second grid of the
/// same shape starts below the first, as a controller's next page would.
/// </remarks>
public sealed class KnobGrid
{
    public const int Fewest = 1, Most = 32;

    public bool On { get; set; }

    public int Columns { get; set; } = 8;

    public int Rows { get; set; } = 2;

    /// <summary>Brings a hand-edited file's numbers into range.</summary>
    public void Clamp()
    {
        Columns = Math.Clamp(Columns, Fewest, Most);
        Rows = Math.Clamp(Rows, Fewest, Most);
    }

    /// <summary>"8x2" for eight columns and two rows, or "off": what <see cref="Read"/> reads.</summary>
    public override string ToString() => On ? $"{Columns}x{Rows}" : "off";

    /// <summary>A grid from "COLUMNSxROWS" or "off", or null for anything else or a side out of range.</summary>
    public static KnobGrid? Read(string text)
    {
        if (text.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return new KnobGrid();

        var sides = text.Split('x', 'X');

        return sides.Length == 2
            && int.TryParse(sides[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var columns)
            && int.TryParse(sides[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rows)
            && columns is >= Fewest and <= Most
            && rows is >= Fewest and <= Most
                ? new KnobGrid { On = true, Columns = columns, Rows = rows }
                : null;
    }
}
