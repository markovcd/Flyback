using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Editor.Sequence;
using Shouldly;

namespace Flyback.Editor.Tests.Sequence;

/// <summary>The Song's rows under the pointer.</summary>
public class SongRowsTests : EditorTest
{
    private (Window Window, SongRows Rows, List<string?> Edits) Showing()
    {
        var rows = new SongRows
        {
            Height = 200,
            Parts = new SongParts([new("kick", [new(1f), new(0f), new(1f), new(0f)]), new("bass", [new(0f), new(0f)])]),
        };

        var edits = new List<string?>();
        rows.Edited += edits.Add;

        var window = Show(rows, width: 600);
        return (window, rows, edits);
    }

    /// <summary>The middle of a section's cell in a part's row, in the window.</summary>
    private static Point Cell(SongRows rows, int part, int section) =>
        new(rows.Layout.X(section + 0.5), rows.Layout.RowTop(part) + rows.Layout.Row / 2);

    [AvaloniaFact]
    public void A_click_on_a_cell_switches_its_level()
    {
        var (window, rows, edits) = Showing();

        window.MouseDown(Cell(rows, 0, 1), MouseButton.Left);
        window.MouseUp(Cell(rows, 0, 1), MouseButton.Left);

        rows.Parts.Levels(0)[1].Value.ShouldBe(1f);
        edits.ShouldHaveSingleItem();
    }

    [AvaloniaFact]
    public void A_drag_up_turns_a_level()
    {
        var (window, rows, _) = Showing();
        var from = Cell(rows, 1, 0);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, 40));
        window.MouseMove(from - new Point(0, 80));
        window.MouseUp(from - new Point(0, 80), MouseButton.Left);

        rows.Parts.Levels(1)[0].Value.ShouldBe(0.5f, tolerance: 0.01f);
    }

    [AvaloniaFact]
    public void A_drag_along_paints_the_first_cell_s_level()
    {
        var (window, rows, _) = Showing();

        window.MouseDown(Cell(rows, 0, 0), MouseButton.Left);
        window.MouseMove(Cell(rows, 0, 1));
        window.MouseMove(Cell(rows, 0, 3));
        window.MouseUp(Cell(rows, 0, 3), MouseButton.Left);

        rows.Parts.Levels(0).Select(level => level.Value).ShouldBe([1f, 1f, 1f, 1f]);
    }

    [AvaloniaFact]
    public void Dragging_a_part_s_handle_sets_how_long_it_loops()
    {
        var (window, rows, edits) = Showing();
        var handle = new Point(rows.Layout.X(2), Cell(rows, 1, 0).Y);

        window.MouseDown(handle, MouseButton.Left);
        window.MouseMove(new Point(rows.Layout.X(5), handle.Y));
        window.MouseUp(new Point(rows.Layout.X(5), handle.Y), MouseButton.Left);

        rows.Parts.Levels(1).Count.ShouldBe(5);
        edits.ShouldBe(["part 1 length"]);
    }

    [AvaloniaFact]
    public void Pressing_the_ruler_seeks_to_that_section()
    {
        var (window, rows, _) = Showing();
        var sought = new List<double>();
        rows.Sought += sought.Add;

        window.MouseDown(new Point(rows.Layout.X(3.5), 10), MouseButton.Left);

        sought.ShouldBe([3d]);
    }

    [AvaloniaFact]
    public void Each_part_s_playhead_comes_round_at_its_own_length()
    {
        var (_, rows, _) = Showing();

        rows.Played = 5.5;

        rows.Head(0).ShouldBe(1.5);
        rows.Head(1).ShouldBe(1.5);

        rows.Played = 6.25;

        rows.Head(0).ShouldBe(2.25);
        rows.Head(1).ShouldBe(0.25);
    }
}
