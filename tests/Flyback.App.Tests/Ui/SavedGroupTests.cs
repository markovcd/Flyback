using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Keeping a group, adding it again, and taking it off the list.
/// </summary>
/// <remarks>
/// The two ends of one feature, which live at opposite ends of the window: the
/// button that keeps a box is in the panel on the right, and what it keeps shows
/// up in the module list, which is a popup over the canvas. Nothing joins them
/// but a folder on the disk — a folder these tests point somewhere harmless,
/// because the usual one is where a person's own groups are.
/// </remarks>
public class SavedGroupTests : UiTest, IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-kept-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>A window with a two-module box in it, selected and named.</summary>
    private MainWindow Open(out NodeGroup group, string? named = "Voice")
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 40, 200);
        var osc = b.Add("osc.sine", 300, 60);
        var screen = b.Add(NodeCatalog.OutputTypeId, 900, 200);

        b.Wire(time, 0, osc, 1).Wire(osc, 0, screen, NodeCatalog.OutputLeftPort);

        var window = new MainWindow(folder);

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var editor = Editor(window);
        editor.Patch = b.Patch;
        Settle(window);

        var patch = editor.Patch;

        group = patch.Group([time.Id, osc.Id]) ?? throw new InvalidOperationException("no group");
        group.Rename(named);

        editor.NotifyPatchChanged();
        SelectBox(window, patch, group);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static ModulePalette Palette(MainWindow window) => All<ModulePalette>(window).Single();

    private static void SelectBox(MainWindow window, Patch patch, NodeGroup group)
    {
        var editor = Editor(window);
        var bounds = NodeGeometry.GroupBounds(patch, group, patch.SocketsOf(group));
        var header = new Point(bounds.Center.X, bounds.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(header), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    /// <summary>Opens the module list the way a person does: a right-click on bare canvas.</summary>
    private static void OpenList(MainWindow window)
    {
        var editor = Editor(window);

        var at = editor.TranslatePoint(new Point(24, editor.Bounds.Height - 24), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Settle(window);
    }

    private static Button Button(Visual root, string caption) =>
        All<Button>(root).First(b => b.Content as string == caption);

    private static void Press(MainWindow window, Visual root, string caption)
    {
        var button = Button(root, caption);

        button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Settle(window);
    }

    /// <summary>Escape, at whatever currently has the keyboard.</summary>
    private static void Escape(MainWindow window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);
    }

    private static string[] Captions(Visual root) =>
        [.. All<Button>(root).Select(b => b.Content as string ?? string.Empty)];

    private static string[] Lines(Visual root) =>
        [.. All<TextBlock>(root).Select(t => t.Text ?? string.Empty)];

    private static TextBlock Line(Visual root, string text) =>
        All<TextBlock>(root).First(t => t.Text == text);

    /// <summary>How far down <paramref name="root"/> the middle of a control is.</summary>
    private static double Middle(Visual root, Visual control) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), root)?.Y
        ?? throw new InvalidOperationException("the control is not under that root");

    // --- keeping one --------------------------------------------------------

    /// <summary>
    /// The list calls a kept group by its name, so a group with none cannot be
    /// kept — and the button says so rather than being missing, because a button
    /// that is not there answers no questions about why.
    /// </summary>
    [AvaloniaFact]
    public void A_group_with_no_name_is_offered_the_button_greyed()
    {
        var window = Open(out var group, named: null);

        var greyed = Button(window, "Save to palette");

        greyed.IsEnabled.ShouldBeFalse();

        // And it still explains itself. A tip that will not show on a disabled
        // control explains it to nobody, and the greyed button is the one with
        // something to explain.
        ToolTip.GetShowOnDisabled(greyed).ShouldBeTrue();
        ToolTip.GetTip(greyed).ShouldNotBeNull();

        group.Rename("Voice");
        Editor(window).NotifyPatchChanged();
        Settle(window);

        Button(window, "Save to palette").IsEnabled.ShouldBeTrue("named, it can be kept");
    }

    [AvaloniaFact]
    public void Keeping_a_group_puts_it_on_the_module_list_under_Groups()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        OpenList(window);

        var palette = Palette(window);

        Lines(palette).ShouldContain("GROUPS");
        Captions(palette).ShouldContain("Voice");
    }

    /// <summary>
    /// The whole point of keeping one: what comes back is a box, not the modules
    /// that were in it.
    /// </summary>
    [AvaloniaFact]
    public void Picking_a_kept_group_adds_it_as_a_box()
    {
        var window = Open(out _);
        var editor = Editor(window);

        Press(window, window, "Save to palette");
        OpenList(window);
        Press(window, Palette(window), "Voice");

        editor.Patch.Nodes.Count(n => n.TypeId == "time").ShouldBe(2, "a second copy arrived");
        editor.Patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);

        var groups = editor.Patch.Groups.ShouldNotBeNull();

        groups.Count.ShouldBe(2, "the box came with the modules");
        groups.Select(g => g.Name).ShouldAllBe(name => name == "Voice");
        groups.Select(g => g.Id).Distinct().Count().ShouldBe(2, "and it is a second box");
        groups.ShouldAllBe(g => g.Collapsed, "a kept group arrives shut, which is what makes it one thing");
    }

    // --- taking one off -----------------------------------------------------

    /// <summary>
    /// The ✕ asks first. A kept group is a file and there is no undo out here to
    /// take one back with, so the row turns into its own question rather than
    /// the entry simply going.
    /// </summary>
    [AvaloniaFact]
    public void The_cross_asks_before_it_takes_one_off_the_list()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        OpenList(window);

        var palette = Palette(window);

        Press(window, palette, "✕");

        Lines(palette).ShouldContain("Remove “Voice”?");
        Captions(palette).ShouldContain("✔", "the tick removes it");
        Captions(palette).ShouldContain("✕", "and the cross puts it back");
        Captions(palette).ShouldNotContain("Voice", "the row is the question while it is being asked");

        // One row and not two: the question and its answers sit at the same
        // height, so the list does not jump under the hand that reached for it.
        Middle(palette, Line(palette, "Remove “Voice”?"))
            .ShouldBe(Middle(palette, Button(palette, "✔")), tolerance: 2);

        Press(window, palette, "✕");

        Captions(palette).ShouldContain("Voice", "cancelling leaves it exactly where it was");
        Directory.GetFiles(folder).Length.ShouldBe(1);
    }

    [AvaloniaFact]
    public void Removing_one_takes_it_off_the_list_and_off_the_disk()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        OpenList(window);

        var palette = Palette(window);

        Press(window, palette, "✕");
        Press(window, palette, "✔");

        Captions(palette).ShouldNotContain("Voice");
        Directory.GetFiles(folder).ShouldBeEmpty("the entry was the file");
    }

    /// <summary>
    /// Escape is the way out of the list, and it stays the way out while a row
    /// is mid-question: the question goes back to being a row and the popup
    /// closes on the same press.
    /// </summary>
    /// <remarks>
    /// A question nobody answered must not be waiting on the list the next time
    /// it is opened — and Escape needing two presses to leave, because something
    /// somewhere was half-asked, would have stopped being the way out.
    /// </remarks>
    [AvaloniaFact]
    public void Escape_takes_the_question_back_and_still_leaves_the_list()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        OpenList(window);

        var palette = Palette(window);

        Press(window, palette, "✕");
        Captions(palette).ShouldContain("✔", "a question is standing");

        Escape(window);

        // The list is gone, which is what Escape has always done to it — a
        // standing question does not turn it into the key that only cancels.
        All<ModulePalette>(window).ShouldBeEmpty("the popup closed on the same press");

        // Nothing was removed: the question was not answered, it was abandoned.
        Directory.GetFiles(folder).Length.ShouldBe(1);

        // And what comes back is the list, not the question waiting where it
        // was left.
        OpenList(window);

        var again = Palette(window);

        Captions(again).ShouldContain("Voice");
        Captions(again).ShouldNotContain("✔", "the question did not survive the way out");
    }

    /// <summary>
    /// The same press, with something in the filter box. Escape empties that
    /// first — which is what it has always done, and one press does both.
    /// </summary>
    [AvaloniaFact]
    public void Escape_takes_the_question_back_even_while_it_is_clearing_the_filter()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        OpenList(window);

        var palette = Palette(window);
        var filter = All<TextBox>(palette).First();

        filter.Text = "Voi";
        Settle(window);

        Press(window, palette, "✕");
        Captions(palette).ShouldContain("✔");

        filter.Focus();
        Escape(window);

        filter.Text.ShouldBeNullOrEmpty("the box empties, as it always did");
        Captions(palette).ShouldNotContain("✔", "and the question goes with it");
        Captions(palette).ShouldContain("Voice", "the row is back");
    }

    // --- keeping one over another -------------------------------------------

    /// <summary>
    /// Saving under a name already kept replaces what is there, and replacing is
    /// a file going for good — so it asks, in the place the button was standing.
    /// </summary>
    [AvaloniaFact]
    public void Keeping_one_over_a_name_already_kept_asks_first()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");
        Directory.GetFiles(folder).Length.ShouldBe(1);

        Press(window, window, "Save to palette");

        Lines(window).ShouldContain("Replace “Voice”?");
        Captions(window).ShouldContain("✔");
        Captions(window).ShouldNotContain("Save to palette", "the button is the question while it is asked");

        // One row, at the height the button was: the panel does not move under
        // the hand that has just pressed it.
        Middle(window, Line(window, "Replace “Voice”?"))
            .ShouldBe(Middle(window, Button(window, "✔")), tolerance: 2);
    }

    /// <summary>
    /// The answer that backs out leaves the kept group exactly as it was, which
    /// is the whole reason for asking.
    /// </summary>
    [AvaloniaFact]
    public void Backing_out_of_a_replacement_keeps_what_was_there()
    {
        var window = Open(out _);
        var editor = Editor(window);

        Press(window, window, "Save to palette");

        var before = File.ReadAllText(Directory.GetFiles(folder).Single());

        // A different group under the same name, which is exactly how somebody
        // loses one by accident.
        editor.DeleteSelected();

        var other = editor.AddNode("math.mul", new Point(200, 400)).ShouldNotBeNull();
        var third = editor.AddNode("value", new Point(400, 400)).ShouldNotBeNull();

        var second = editor.Patch.Group([other.Id, third.Id]).ShouldNotBeNull();

        second.Rename("Voice");
        editor.NotifyPatchChanged();
        SelectBox(window, editor.Patch, second);

        Press(window, window, "Save to palette");
        Press(window, window, "✕");

        Directory.GetFiles(folder).Length.ShouldBe(1);
        File.ReadAllText(Directory.GetFiles(folder).Single()).ShouldBe(before, "nothing was written");

        // And the button is back, ready to be pressed on purpose this time.
        Captions(window).ShouldContain("Save to palette");
    }

    [AvaloniaFact]
    public void Saying_yes_replaces_it()
    {
        var window = Open(out _);
        var editor = Editor(window);

        Press(window, window, "Save to palette");

        var before = File.ReadAllText(Directory.GetFiles(folder).Single());

        editor.DeleteSelected();

        var other = editor.AddNode("math.mul", new Point(200, 400)).ShouldNotBeNull();
        var third = editor.AddNode("value", new Point(400, 400)).ShouldNotBeNull();

        var second = editor.Patch.Group([other.Id, third.Id]).ShouldNotBeNull();

        second.Rename("Voice");
        editor.NotifyPatchChanged();
        SelectBox(window, editor.Patch, second);

        Press(window, window, "Save to palette");
        Press(window, window, "✔");

        var files = Directory.GetFiles(folder);

        files.Length.ShouldBe(1, "the same name is the same file");
        File.ReadAllText(files.Single()).ShouldNotBe(before, "and it is the new group in it");

        OpenList(window);
        Captions(Palette(window)).ShouldContain("Voice");
    }

    /// <summary>
    /// Nothing is asked where nothing would be replaced: a name nobody has used
    /// is kept on the first press, which is the ordinary case.
    /// </summary>
    [AvaloniaFact]
    public void A_name_nobody_has_used_is_kept_without_a_question()
    {
        var window = Open(out _);

        Press(window, window, "Save to palette");

        Lines(window).ShouldNotContain("Replace “Voice”?");
        Directory.GetFiles(folder).Length.ShouldBe(1);
    }
}
