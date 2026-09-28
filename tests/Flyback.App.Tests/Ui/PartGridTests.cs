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

    private static void Click(Window window, string label) =>
        All<Button>(window).Single(b => Equals(b.Content, label))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
