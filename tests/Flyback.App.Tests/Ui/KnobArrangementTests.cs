using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>Where knobs stand, wrapped to a width or held in a fixed grid.</summary>
public class KnobArrangementTests
{
    private const double Side = 50, Gap = 10;

    private static KnobArrangement Knobs(int count, KnobGrid? grid)
    {
        var knobs = new KnobArrangement { ItemSpacing = Gap, LineSpacing = Gap, Grid = grid };

        for (var i = 0; i < count; i++) knobs.Children.Add(new Border { Width = Side, Height = Side });

        return knobs;
    }

    private static Point At(KnobArrangement knobs, int index, double width)
    {
        knobs.Measure(new Size(width, double.PositiveInfinity));
        knobs.Arrange(new Rect(new Size(Math.Max(width, knobs.DesiredSize.Width), knobs.DesiredSize.Height)));

        return knobs.Children[index].Bounds.Position;
    }

    [AvaloniaFact]
    public void Wrapped_knobs_take_as_many_to_a_row_as_the_width_holds()
    {
        At(Knobs(6, null), 2, width: 1000).ShouldBe(new Point(2 * (Side + Gap), 0));
        At(Knobs(6, null), 2, width: 120).ShouldBe(new Point(0, Side + Gap));
    }

    [AvaloniaFact]
    public void A_grid_knob_keeps_its_row_and_column_whatever_the_width()
    {
        var grid = new KnobGrid { On = true, Columns = 4, Rows = 2 };

        At(Knobs(6, grid), 4, width: 1000).ShouldBe(new Point(0, Side + Gap));
        At(Knobs(6, grid), 4, width: 120).ShouldBe(new Point(0, Side + Gap));
        At(Knobs(6, grid), 3, width: 120).ShouldBe(new Point(3 * (Side + Gap), 0));
    }

    [AvaloniaFact]
    public void Past_the_last_row_the_next_grid_starts_a_bank_apart()
    {
        var knobs = Knobs(9, new KnobGrid { On = true, Columns = 4, Rows = 2 });

        At(knobs, 8, width: 1000).ShouldBe(new Point(0, 2 * Side + Gap + 4 * Gap));
    }

    [AvaloniaFact]
    public void A_grid_is_as_big_as_its_banks_however_few_knobs_fill_them()
    {
        var knobs = Knobs(1, new KnobGrid { On = true, Columns = 4, Rows = 2 });

        knobs.Measure(Size.Infinity);

        knobs.DesiredSize.ShouldBe(new Size(4 * Side + 3 * Gap, 2 * Side + Gap));
    }

    [AvaloniaFact]
    public void A_grid_switched_off_wraps()
    {
        var knobs = Knobs(6, new KnobGrid { On = false, Columns = 4, Rows = 2 });

        knobs.Grid.ShouldBeNull();
        At(knobs, 2, width: 120).ShouldBe(new Point(0, Side + Gap));
    }
}
