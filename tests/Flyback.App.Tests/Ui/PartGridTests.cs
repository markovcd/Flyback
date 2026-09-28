using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Inspect;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>The grid an Arrangement's parts are edited in.</summary>
public class PartGridTests : UiTest
{
    private Window Showing(out NodeInstance node, out List<string?> changes)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        node = NodeInstance.Create(def, 0, 0);

        var said = new List<string?>();
        changes = said;

        return Show(new PartGrid(node, def, said.Add).View);
    }

    private static TextBox[] Rows(Window window) =>
        [.. All<TextBox>(window).Where(box => Equals(box.Tag, PartGrid.RowTag))];

    [AvaloniaFact]
    public void Every_part_gets_a_row_and_every_level_a_cell()
    {
        var window = Showing(out var node, out _);
        var parts = ArrangementExtra.Of(node);

        Rows(window).Length.ShouldBe(parts.Count);
        All<Border>(window).Count(b => Equals(b.Tag, PartGrid.CellTag)).ShouldBe(parts.Count * parts[0].Count);
        Rows(window)[1].Text.ShouldBe("0 1 1 1");
    }

    [AvaloniaFact]
    public void Levels_typed_into_a_row_are_kept_on_Enter()
    {
        var window = Showing(out var node, out var changes);
        var row = Rows(window)[0];

        row.Focus();
        row.Text = "0 >1 0.5 0 1";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        var parts = ArrangementExtra.Of(node);

        parts[0].ShouldBe([new(0f), new(1f, Glides: true), new(0.5f), new(0f), new(1f)]);
        parts.ShouldAllBe(part => part.Count == 5);
        changes.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public void A_row_that_does_not_read_is_left_as_it_was()
    {
        var window = Showing(out var node, out var changes);
        var before = ArrangementExtra.Of(node);
        var row = Rows(window)[0];

        row.Focus();
        row.Text = "1 loud";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        ArrangementExtra.Of(node).ShouldBe(before);
        changes.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void A_part_and_a_section_can_be_added()
    {
        var window = Showing(out var node, out _);

        Click(window, "+ part");
        Click(window, "+ section");

        var parts = ArrangementExtra.Of(node);

        parts.Count.ShouldBe(4);
        parts.ShouldAllBe(part => part.Count == 5);
        Rows(window).Length.ShouldBe(4);
    }

    /// <summary>Cells are counted across, then down: part 1 is cells 0 and 1, part 2 cells 2 and 3.</summary>
    [AvaloniaFact]
    public void Clicking_a_cell_switches_it_off_and_back_on_at_the_parts_level()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var changes = new List<string?>();

        ArrangementExtra.Set(node, [[new PartLevel(0.8f), new PartLevel(0.5f)], [new PartLevel(0f), new PartLevel(0f)]]);
        var window = Show(new PartGrid(node, def, changes.Add).View);

        ClickCell(window, 0);
        ArrangementExtra.Of(node)[0].Select(l => l.Value).ShouldBe([0f, 0.5f]);

        ClickCell(window, 0);
        ClickCell(window, 2);
        var parts = ArrangementExtra.Of(node);

        parts[0].Select(l => l.Value).ShouldBe([0.5f, 0.5f]);
        parts[1].Select(l => l.Value).ShouldBe([1f, 0f]);
        changes.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public void Dragging_a_cell_up_turns_its_level_up_and_down_turns_it_down()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var changes = new List<string?>();

        ArrangementExtra.Set(node, [[new PartLevel(0.5f), new PartLevel(1f)]]);
        var window = Show(new PartGrid(node, def, changes.Add).View);

        // Half the travel up is half the range: 0.5 to 1.
        Drag(window, 0, -80);
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(1f, 0.02f);

        // Far past the bottom stops at nought.
        Drag(window, 0, 400);
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(0f);

        changes.Distinct().Count().ShouldBe(1, "a drag is one step in the history, however far it goes");
    }

    private static void Drag(Window window, int index, double by)
    {
        var cell = All<Border>(window).Where(b => Equals(b.Tag, PartGrid.CellTag)).ElementAt(index);
        var from = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        for (var step = 1; step <= 10; step++)
            window.MouseMove(from + new Point(0, by * step / 10), RawInputModifiers.LeftMouseButton);
        window.MouseUp(from + new Point(0, by), MouseButton.Left, RawInputModifiers.None);
    }

    /// <summary>The <paramref name="index"/>th cell of the map, counted across then down, clicked in its middle.</summary>
    private static void ClickCell(Window window, int index)
    {
        var cell = All<Border>(window).Where(b => Equals(b.Tag, PartGrid.CellTag)).ElementAt(index);
        var middle = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;

        window.MouseDown(middle, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(middle, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Click(Window window, string label) =>
        All<Button>(window).Single(b => Equals(b.Content, label))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
