using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Laying out only what is selected: Ctrl+Shift+L, and Ctrl+click on the toolbar's
/// layout button. The rest of the patch stays exactly where it is, which is the
/// whole reason to press the narrower of the two (ADR-0110).
/// </summary>
public class TidySelectionTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    /// <summary>
    /// Two chains far apart, each tangled enough that laying it out moves it, and
    /// each far enough from the other that neither is in the other's way.
    /// </summary>
    private static Patch Apart(out NodeInstance[] near, out NodeInstance[] far)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 400, -700);
        var osc = b.Add("osc.sine", -600, -300, (1, 220f));

        var coord = b.Add("coord", 1600, 900);
        var rings = b.Add("pattern.rings", 1100, 1400);

        var sink = b.Add(NodeCatalog.OutputTypeId, 2200, 0);

        b.Wire(time, 0, osc, 0)
         .Wire(osc, 0, sink, NodeCatalog.OutputLeftPort)
         .Wire(coord, 0, rings, 0)
         .Wire(rings, 0, sink, NodeCatalog.OutputColorPort);

        near = [time, osc];
        far = [coord, rings];

        return b.Patch;
    }

    private (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    private static Dictionary<Guid, (double X, double Y)> Where(Patch patch) =>
        patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

    private static void Pick(NodeEditor editor, Window window, params NodeInstance[] nodes)
    {
        var held = RawInputModifiers.None;

        foreach (var node in nodes)
        {
            var body = new Point(
                node.X + NodeGeometry.Width / 2,
                node.Y + NodeGeometry.HeaderHeight / 2);

            var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
                ?? throw new InvalidOperationException("the editor is not in this window");

            window.MouseDown(at, MouseButton.Left, held);
            window.MouseUp(at, MouseButton.Left, held);
            Settle(window);

            held = RawInputModifiers.Control;
        }
    }

    [AvaloniaFact]
    public void Laying_out_the_selection_leaves_the_rest_of_the_patch_where_it_was()
    {
        var patch = Apart(out var near, out var far);
        var (editor, window) = Editing(patch);

        Pick(editor, window, near);

        var before = Where(patch);

        editor.Tidy(onlySelected: true);

        foreach (var node in far)
            (node.X, node.Y).ShouldBe(before[node.Id], $"{node.TypeId} is not selected");

        near.ShouldContain(
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            n => n.X != before[n.Id].X || n.Y != before[n.Id].Y,
            "the selection should have been laid out");
    }

    /// <summary>And the whole button still lays out the whole patch.</summary>
    [AvaloniaFact]
    public void Laying_out_without_the_modifier_still_moves_everything()
    {
        var patch = Apart(out var near, out var far);
        var (editor, window) = Editing(patch);

        Pick(editor, window, near);

        var before = Where(patch);

        editor.Tidy();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        far.ShouldContain(n => n.X != before[n.Id].X || n.Y != before[n.Id].Y);
    }

    /// <summary>
    /// It is one edit, the same as the whole layout is: a button that took two
    /// presses of Ctrl+Z to put back would be worse than no button.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_the_selection_is_a_single_undo()
    {
        var patch = Apart(out var near, out _);
        var (editor, window) = Editing(patch);

        Pick(editor, window, near);

        var before = Where(patch);

        editor.Tidy(onlySelected: true);
        editor.Undo().ShouldBeTrue();

        foreach (var node in editor.Patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    /// <summary>
    /// And it leaves the view alone. The selection went back where it was, so a
    /// canvas that framed the patch would take the part being worked on out from
    /// under the eye that was on it.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_the_selection_leaves_the_view_alone()
    {
        var patch = Apart(out var near, out _);
        var (editor, window) = Editing(patch);

        Pick(editor, window, near);

        var before = editor.GraphToScreen;

        editor.Tidy(onlySelected: true);
        Settle(window);

        editor.GraphToScreen.ShouldBe(before);
    }

    /// <summary>
    /// With nothing selected there is nothing to lay out, and it says so rather
    /// than quietly laying out the whole patch — which is the other key.
    /// </summary>
    [AvaloniaFact]
    public void Laying_out_an_empty_selection_says_so_and_moves_nothing()
    {
        var patch = Apart(out _, out _);
        var (editor, _) = Editing(patch);

        var said = string.Empty;
        editor.Reported += (_, message) => said = message;

        var before = Where(patch);

        editor.Tidy(onlySelected: true);

        said.ShouldContain("Nothing is selected");

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    // --- the key and the button ---------------------------------------------

    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static Button Tidy(MainWindow window) =>
        All<Button>(window).Single(b => b.Name == "tidy");

    /// <summary>A patch of two chains, opened in the real window.</summary>
    private (MainWindow Window, NodeInstance[] Near, NodeInstance[] Far) Opened()
    {
        var window = Open();
        var patch = Apart(out var near, out var far);

        Editor(window).Patch = patch;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window, near, far);
    }

    private static void Press(MainWindow window, RawInputModifiers modifiers)
    {
        var button = Tidy(window);

        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the button is not in this window");

        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Control_shift_l_lays_out_only_the_selection()
    {
        var (window, near, far) = Opened();

        Pick(Editor(window), window, near);

        var before = Where(Editor(window).Patch);

        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.L,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
        });

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        foreach (var node in far)
            (node.X, node.Y).ShouldBe(before[node.Id], "only the selection moves");

        near.ShouldContain(
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            n => n.X != before[n.Id].X || n.Y != before[n.Id].Y,
            "the selection should have been laid out");
    }

    /// <summary>And Ctrl+L still lays out the whole patch, selection or no selection.</summary>
    [AvaloniaFact]
    public void Control_l_lays_out_the_whole_patch()
    {
        var (window, near, far) = Opened();

        Pick(Editor(window), window, near);

        var before = Where(Editor(window).Patch);

        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.L,
            KeyModifiers = KeyModifiers.Control,
        });

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        far.ShouldContain(n => n.X != before[n.Id].X || n.Y != before[n.Id].Y);
    }

    /// <summary>
    /// Ctrl held on the button is the same thing as Ctrl+Shift+L. What a Click
    /// carries says nothing about what was held down, so the modifier is read off
    /// the press on its way in.
    /// </summary>
    [AvaloniaFact]
    public void Control_click_on_the_layout_button_lays_out_only_the_selection()
    {
        var (window, near, far) = Opened();

        Pick(Editor(window), window, near);

        var before = Where(Editor(window).Patch);

        Press(window, RawInputModifiers.Control);

        foreach (var node in far)
            (node.X, node.Y).ShouldBe(before[node.Id], "only the selection moves");

        near.ShouldContain(
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            n => n.X != before[n.Id].X || n.Y != before[n.Id].Y,
            "the selection should have been laid out");
    }

    /// <summary>And a plain press of the same button lays out the whole patch.</summary>
    [AvaloniaFact]
    public void A_plain_press_of_the_layout_button_lays_out_the_whole_patch()
    {
        var (window, near, far) = Opened();

        Pick(Editor(window), window, near);

        var before = Where(Editor(window).Patch);

        Press(window, RawInputModifiers.None);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        far.ShouldContain(n => n.X != before[n.Id].X || n.Y != before[n.Id].Y);
    }

    /// <summary>
    /// A modifier nobody can see is a modifier nobody finds, so the tip names
    /// both of the things the button does.
    /// </summary>
    [AvaloniaFact]
    public void The_layout_buttons_tip_says_what_control_click_does()
    {
        var tip = ToolTip.GetTip(Tidy(Open())) as string;

        tip.ShouldNotBeNull();
        tip.ShouldContain("Ctrl+click");
        tip.ShouldContain("Ctrl+Shift+L");
    }
}
