using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Ui.Controls;
using Flyback.Editor.Inspect;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests.Ui;

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

    private static Button[] Removers(Window window) =>
        [.. All<Button>(window).Where(button => Equals(button.Tag, PartGrid.RemoveTag))];

    [AvaloniaFact]
    public void Every_part_gets_a_row_and_every_level_a_cell()
    {
        var window = Showing(out var node, out _);
        var parts = ArrangementExtra.Of(node);

        Removers(window).Length.ShouldBe(parts.Count);
        All<Border>(window).Count(b => Equals(b.Tag, PartGrid.CellTag)).ShouldBe(parts.Count * parts[0].Count);
        All<TextBox>(window).ShouldBeEmpty();
    }

    /// <summary>Shaded against nought and one, not against the rest of its part.</summary>
    [AvaloniaFact]
    public void A_level_is_shaded_the_same_whatever_else_its_part_holds()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);

        Avalonia.Media.Color ShadeOf(params float[] levels)
        {
            var node = NodeInstance.Create(def, 0, 0);
            ArrangementExtra.Set(node, [levels.Select(level => new PartLevel(level))]);

            var window = Show(new PartGrid(node, def, _ => { }).View);
            var cell = All<Border>(window).First(b => Equals(b.Tag, PartGrid.CellTag));

            return ((Avalonia.Media.ISolidColorBrush)cell.Background!).Color;
        }

        ShadeOf(0.5f, 0.5f).ShouldBe(ShadeOf(0.5f, 1f));
        ShadeOf(0.5f, 0.5f).ShouldNotBe(ShadeOf(1f, 1f));
        ShadeOf(1f, 0f).ShouldBe(ShadeOf(12f, 0f));

        // Below nought is another color, as strong as the level is far from nought.
        ShadeOf(-1f, 0f).ShouldNotBe(ShadeOf(1f, 0f));
        ShadeOf(-0.5f, 0f).ShouldNotBe(ShadeOf(-1f, 0f));
    }

    [AvaloniaFact]
    public void A_parts_cross_takes_it_away()
    {
        var window = Showing(out var node, out var changes);

        Removers(window)[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var parts = ArrangementExtra.Of(node);

        parts.Count.ShouldBe(2);
        parts[0].Select(l => l.Value).ShouldBe([0f, 1f, 1f, 1f]);
        Removers(window).Length.ShouldBe(2);
        changes.ShouldNotBeEmpty();
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
        Removers(window).Length.ShouldBe(4);
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

        // Another cell between, since the same one twice is a double-click.
        ClickCell(window, 2);
        ClickCell(window, 0);
        var parts = ArrangementExtra.Of(node);

        parts[0].Select(l => l.Value).ShouldBe([0.5f, 0.5f]);
        parts[1].Select(l => l.Value).ShouldBe([1f, 0f]);
        changes.Count.ShouldBe(3);
    }

    [AvaloniaFact]
    public void Double_clicking_a_cell_makes_its_level_glide_and_again_hold()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var changes = new List<string?>();

        ArrangementExtra.Set(node, [[new PartLevel(0f), new PartLevel(1f)]]);
        var window = Show(new PartGrid(node, def, changes.Add).View);

        ClickCell(window, 1);
        ClickCell(window, 1);
        ArrangementExtra.Of(node)[0][1].ShouldBe(new PartLevel(1f, Glides: true));

        ClickCell(window, 1);
        ClickCell(window, 1);
        ArrangementExtra.Of(node)[0][1].ShouldBe(new PartLevel(1f));

        changes.Distinct().Count().ShouldBe(1, "a double-click and the click it began with are one step");
    }

    [AvaloniaFact]
    public void Dragging_a_cell_turns_its_level_up_and_down_holding_at_nought_on_the_way()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var changes = new List<string?>();

        ArrangementExtra.Set(node, [[new PartLevel(0.5f), new PartLevel(1f)]]);
        var window = Show(new PartGrid(node, def, changes.Add).View);

        // Half the travel up is half the range: 0.5 to 1.
        Drag(window, 0, -80);
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(1f, 0.02f);

        // A whole travel down from 1 is nought, and a few pixels past it are held there.
        Drag(window, 0, 165);
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(0f, 0.001f);

        // On past the catch it goes below nought, as far as the part reaches above it.
        Drag(window, 0, 400);
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(-1f);

        changes.Distinct().Count().ShouldBe(1, "a drag is one step in the history, however far it goes");
    }

    /// <summary>
    /// Each time the patch is told, it recompiles both programs, which a heavy patch cannot
    /// do at the pointer's rate, so a drag tells it once, when it is let go.
    /// </summary>
    [AvaloniaFact]
    public void A_drag_tells_the_patch_once_when_it_is_let_go()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var changes = new List<string?>();

        ArrangementExtra.Set(node, [[new PartLevel(0f), new PartLevel(1f)]]);
        var window = Show(new PartGrid(node, def, changes.Add).View);

        Drag(window, 0, -160);

        changes.Count.ShouldBe(1, "ten moves are one recompile, at the end");
        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(1f, 0.02f);
    }

    /// <summary>Stands in for the platform, whose warp arrives back as a move to where the drag began.</summary>
    private sealed class Anchor : IPointerAnchor, IPointerAnchors
    {
        public bool Disposed { get; private set; }

        public IPointerAnchor Take(Visual visual) => this;

        public bool Return() => true;

        public void Dispose() => Disposed = true;
    }

    /// <summary>With the pointer held, a drag keeps turning on a short stretch, as a knob does, and hides the pointer until it ends.</summary>
    [AvaloniaFact]
    public void A_held_pointer_turns_a_level_all_the_way_on_a_short_stretch()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var node = NodeInstance.Create(def, 0, 0);
        var anchor = new Anchor();

        ArrangementExtra.Set(node, [[new PartLevel(0f), new PartLevel(1f)]]);
        var window = Show(new PartGrid(node, def, _ => { }) { Anchors = anchor }.View);

        var cell = All<Border>(window).First(b => Equals(b.Tag, PartGrid.CellTag));
        var middle = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
        var upright = cell.Cursor;

        window.MouseDown(middle, MouseButton.Left, RawInputModifiers.None);

        // Four strokes of 40 px up, the pointer put back after each: 160 px in all.
        for (var stroke = 0; stroke < 4; stroke++)
        {
            window.MouseMove(middle - new Point(0, 40), RawInputModifiers.LeftMouseButton);
            window.MouseMove(middle, RawInputModifiers.LeftMouseButton);
        }

        cell.Cursor.ShouldNotBeSameAs(upright);

        window.MouseUp(middle, MouseButton.Left, RawInputModifiers.None);

        ArrangementExtra.Of(node)[0][0].Value.ShouldBe(1f, 0.02f);
        anchor.Disposed.ShouldBeTrue();
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
