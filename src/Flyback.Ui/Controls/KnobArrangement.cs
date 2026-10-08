using Avalonia;
using Avalonia.Controls;
using Flyback.Host;

namespace Flyback.Ui.Controls;

/// <summary>
/// Lays knobs out in a row that wraps to the width it is given, or, with
/// <see cref="Grid"/> on, in fixed columns and rows that the width never changes.
/// </summary>
/// <remarks>
/// A grid's slots are all as big as the biggest knob, so a knob's place depends on its
/// index alone. Past the last row a second grid starts below, a bank's gap apart.
/// </remarks>
public sealed class KnobArrangement : Panel
{
    private KnobGrid? grid;

    /// <summary>The fixed grid the knobs stand in, or null to wrap.</summary>
    public KnobGrid? Grid
    {
        get => grid;
        set
        {
            grid = value is { On: true } ? new KnobGrid { On = true, Columns = value.Columns, Rows = value.Rows } : null;
            grid?.Clamp();
            InvalidateMeasure();
        }
    }

    /// <summary>The gap between two knobs side by side.</summary>
    public double ItemSpacing { get; init; }

    /// <summary>The gap between two rows.</summary>
    public double LineSpacing { get; init; }

    /// <summary>The gap between one grid and the next below it.</summary>
    private double BankSpacing => LineSpacing * 4;

    /// <summary>The row and column a knob at <paramref name="index"/> stands in, counted from one across every bank.</summary>
    public static (int Row, int Column) Slot(KnobGrid grid, int index) => (index / grid.Columns + 1, index % grid.Columns + 1);

    protected override Size MeasureOverride(Size availableSize)
    {
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);

        foreach (var child in Children) child.Measure(free);

        return grid is { } fixedGrid ? Gridded(fixedGrid, arrange: false) : Wrapped(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (grid is { } fixedGrid) Gridded(fixedGrid, arrange: true);
        else Wrapped(finalSize.Width, arrange: true);

        return finalSize;
    }

    private Size Wrapped(double width, bool arrange)
    {
        double x = 0, y = 0, line = 0, widest = 0;

        foreach (var child in Visible())
        {
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > width)
            {
                y += line + LineSpacing;
                x = 0;
                line = 0;
            }

            if (arrange) child.Arrange(new Rect(new Point(x, y), size));

            x += size.Width + ItemSpacing;
            line = Math.Max(line, size.Height);
            widest = Math.Max(widest, x - ItemSpacing);
        }

        return new Size(widest, y + line);
    }

    private Size Gridded(KnobGrid fixedGrid, bool arrange)
    {
        var knobs = Visible().ToList();

        if (knobs.Count == 0) return default;

        var slotWidth = knobs.Max(child => child.DesiredSize.Width);
        var slotHeight = knobs.Max(child => child.DesiredSize.Height);
        var rows = (knobs.Count + fixedGrid.Columns - 1) / fixedGrid.Columns;
        var banks = (rows + fixedGrid.Rows - 1) / fixedGrid.Rows;

        if (arrange)
            for (var i = 0; i < knobs.Count; i++)
            {
                var (row, column) = Slot(fixedGrid, i);
                var at = new Point((column - 1) * (slotWidth + ItemSpacing), Top(row - 1));

                knobs[i].Arrange(new Rect(at, new Size(slotWidth, slotHeight)));
            }

        // The whole of the last bank, not only its rows with a knob in them, so adding
        // one never makes the panel taller.
        var width = fixedGrid.Columns * (slotWidth + ItemSpacing) - ItemSpacing;
        var height = Top(banks * fixedGrid.Rows - 1) + slotHeight;

        return new Size(width, height);

        double Top(int row) => row * (slotHeight + LineSpacing) + row / fixedGrid.Rows * (BankSpacing - LineSpacing);
    }

    private IEnumerable<Control> Visible() => Children.Where(child => child.IsVisible);
}
