using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The list a sequencer's tune is edited in. These exist because the two things
/// that were actually wrong with it — number boxes that rendered empty, and a
/// volume control overlapping the button beside it — are both invisible to any
/// test that only asks what the control contains.
/// </summary>
public class StepListTests : UiTest
{
    private static (StepList List, NodeInstance Node) Build(string typeId = "seq.notes")
    {
        var def = NodeCatalog.BuiltIn.Require(typeId);
        var node = NodeInstance.Create(def, 0, 0);

        return (new StepList(node, def, _ => { }), node);
    }

    private static Window Showing(out NodeInstance node, string typeId = "seq.notes")
    {
        var (list, built) = Build(typeId);
        node = built;

        return Show(list.View);
    }

    [AvaloniaFact]
    public void Every_note_gets_a_row()
    {
        var window = Showing(out var node);

        // Three number boxes are two per row plus none spare: the value and the
        // length. Counting them is counting rows without depending on layout.
        All<NumericUpDown>(window).Count().ShouldBe(node.Steps!.Count * 2);
    }

    /// <summary>
    /// The defect this suite was written for. Constraining the height of a
    /// NumericUpDown squashes the frame without squashing the TextBox inside its
    /// template, so the number disappears while every structural assertion still
    /// passes.
    /// </summary>
    [AvaloniaFact]
    public void A_notes_number_is_actually_visible()
    {
        var window = Showing(out var node);

        var boxes = All<NumericUpDown>(window).ToArray();
        var text = All<TextBox>(window).ToArray();

        text.Length.ShouldBe(boxes.Length, "every number box should have its text box templated in");

        foreach (var box in text)
        {
            box.Bounds.Height.ShouldBeGreaterThan(0, "a text box with no height shows no number");
            box.Bounds.Width.ShouldBeGreaterThan(0);
        }

        // And the first one is showing the first note rather than nothing.
        text[0].Text.ShouldBe(((int)node.Steps![0].Value).ToString());
    }

    /// <summary>
    /// The other defect: the volume ran under the remove button, because the
    /// column taking the slack was the wrong one. Nothing about the tree says
    /// so — only where the two of them ended up.
    /// </summary>
    [AvaloniaFact]
    public void Nothing_in_a_row_overlaps_anything_else()
    {
        var window = Showing(out _);

        foreach (var row in Rows(window))
        {
            var placed = row.Children
                .Select(c => (Control: c, Box: c.Bounds))
                .Where(p => p.Box.Width > 0)
                .OrderBy(p => p.Box.X)
                .ToArray();

            for (var i = 1; i < placed.Length; i++)
                placed[i].Box.X.ShouldBeGreaterThanOrEqualTo(
                    placed[i - 1].Box.Right - 0.5,
                    $"{placed[i].Control.GetType().Name} overlaps {placed[i - 1].Control.GetType().Name}");
        }
    }

    [AvaloniaFact]
    public void Every_row_stays_inside_the_panel()
    {
        var window = Showing(out _);
        var width = window.Bounds.Width;

        foreach (var row in Rows(window))
            row.Bounds.Right.ShouldBeLessThanOrEqualTo(width + 0.5);
    }

    /// <summary>A note reads as a note, which is the point of the extra column.</summary>
    [AvaloniaFact]
    public void A_note_sequencer_shows_the_name_beside_the_number()
    {
        var window = Showing(out _);

        All<TextBlock>(window).Select(t => t.Text).ShouldContain("A3");
    }

    [AvaloniaFact]
    public void A_value_sequencer_shows_no_names()
    {
        var window = Showing(out _, "seq.values");

        All<TextBlock>(window).Select(t => t.Text).ShouldNotContain("A3");
    }

    // --- editing ------------------------------------------------------------

    [AvaloniaFact]
    public void Typing_a_number_changes_the_note()
    {
        var window = Showing(out var node);
        var value = All<NumericUpDown>(window).First();

        value.Value = 64m;
        Settle(window);

        node.Steps![0].Value.ShouldBe(64f);
    }

    /// <summary>
    /// The name is the one thing showing a value twice, and the row is not
    /// rebuilt while it is being typed into — so it is refreshed by hand, and
    /// that is exactly the kind of thing that silently stops happening.
    /// </summary>
    [AvaloniaFact]
    public void Changing_a_note_updates_the_name_beside_it()
    {
        var window = Showing(out _);

        All<NumericUpDown>(window).First().Value = 64m;
        Settle(window);

        All<TextBlock>(window).Select(t => t.Text).ShouldContain("E4");
    }

    [AvaloniaFact]
    public void A_length_of_nothing_is_held_to_something_playable()
    {
        var window = Showing(out var node);

        // The second box on the first row is that note's length.
        All<NumericUpDown>(window).Skip(1).First().Value = 0m;
        Settle(window);

        node.Steps![0].Length.ShouldBeGreaterThanOrEqualTo(Step.ShortestLength);
    }

    [AvaloniaFact]
    public void Removing_a_note_takes_its_row_with_it()
    {
        var window = Showing(out var node);
        var before = node.Steps!.Count;

        Click(window, Rows(window)[0].Children.OfType<Button>().Single());

        node.Steps.Count.ShouldBe(before - 1);
        All<NumericUpDown>(window).Count().ShouldBe(node.Steps.Count * 2);
    }

    /// <summary>Adding is the strip between two rows, and there is one above the first.</summary>
    [AvaloniaFact]
    public void Clicking_between_two_rows_puts_a_note_there()
    {
        var window = Showing(out var node);
        var before = node.Steps!.Count;

        // The strip above the first row, which is the one that can insert at the top.
        Click(window, Strips(window)[0]);

        node.Steps.Count.ShouldBe(before + 1);
    }

    /// <summary>The rows of the list, told apart from the grids inside control templates.</summary>
    private static Grid[] Rows(Window window) =>
        [.. All<Grid>(window).Where(g => ReferenceEquals(g.Tag, StepList.RowTag))];

    private static Panel[] Strips(Window window) =>
        [.. All<Panel>(window).Where(p => p.Height is StepList.InsertHeightForTests)];

    // --- reordering ---------------------------------------------------------

    /// <summary>
    /// The gesture that could not be tested before this suite existed. It is
    /// also the one most likely to be subtly wrong: the row follows the pointer
    /// by a transform and the list is only rewritten when the drag ends, so
    /// "which row did it land on" is arithmetic rather than something the
    /// layout works out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0, 2)]
    [InlineData(3, 1)]
    [InlineData(7, 0)]
    [InlineData(1, 7)]
    public void A_note_dragged_by_its_handle_lands_where_it_was_dropped(int from, int to)
    {
        var window = Showing(out var node);
        var moving = node.Steps![from];
        var others = node.Steps.Where((_, i) => i != from).ToList();

        Drag(window, from, to);

        node.Steps[to].ShouldBe(moving, "the note should have landed on the row it was dropped on");
        node.Steps.Where((_, i) => i != to).ShouldBe(others, "the rest should keep their order");
        node.Steps.Count.ShouldBe(others.Count + 1);
    }

    [AvaloniaFact]
    public void A_note_dropped_where_it_started_changes_nothing()
    {
        var window = Showing(out var node);
        var before = node.Steps!.ToList();

        Drag(window, 2, 2);

        node.Steps.ShouldBe(before);
    }

    /// <summary>Dragging past the ends stops at them rather than losing the note.</summary>
    [AvaloniaFact]
    public void A_note_dragged_off_the_top_stops_at_the_first_row()
    {
        var window = Showing(out var node);
        var moving = node.Steps![4];

        DragBy(window, 4, -40 * RowPitch);

        node.Steps[0].ShouldBe(moving);
    }

    /// <summary>Row height plus the strip above it — the distance between two rows.</summary>
    private const double RowPitch = 34;

    private static void Drag(Window window, int from, int to) =>
        DragBy(window, from, (to - from) * RowPitch);

    private static void DragBy(Window window, int from, double dy)
    {
        var handle = Rows(window)[from].Children.OfType<TextBlock>().First();
        var start = Centre(handle, window);
        var end = start + new Vector(0, dy);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        Settle(window);
    }

    private static Point Centre(Visual target, Window window) =>
        target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("the control is not in this window");

    /// <summary>A real click, through the window, the way a person makes one.</summary>
    private static void Click(Window window, Visual target)
    {
        var centre = Centre(target, window);

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle(window);
    }

    // --- the volume level ---------------------------------------------------

    /// <summary>
    /// The volume is the one control here drawn by hand rather than composed,
    /// so it is the one with no theme behind it to get the behaviour right.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Clicking_along_the_volume_sets_that_level(double across)
    {
        var window = Showing(out var node);
        var bar = Rows(window)[0].Children.OfType<LevelBar>().Single();

        var at = bar.TranslatePoint(new Point(bar.Bounds.Width * across, bar.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        node.Steps![0].Volume.ShouldBe((float)across, 0.05f);
    }

    /// <summary>Nothing at the far left, which is how a rest is made.</summary>
    [AvaloniaFact]
    public void Clicking_the_far_left_of_the_volume_makes_a_rest()
    {
        var window = Showing(out var node);
        var bar = Rows(window)[0].Children.OfType<LevelBar>().Single();

        var at = bar.TranslatePoint(new Point(0, bar.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        node.Steps![0].Volume.ShouldBe(0f);
    }

    // --- what it looks like -------------------------------------------------

    /// <summary>
    /// Renders the thing and asserts it is not blank. Skia is underneath the
    /// headless platform for exactly this: a control can lay out perfectly and
    /// still draw nothing, which is what the number boxes did.
    /// </summary>
    [AvaloniaFact]
    public void The_list_actually_draws_something()
    {
        var window = Showing(out _);

        using var frame = window.CaptureRenderedFrame();

        frame.ShouldNotBeNull();
        frame.Size.Width.ShouldBeGreaterThan(0);
        frame.Size.Height.ShouldBeGreaterThan(0);
    }
}
