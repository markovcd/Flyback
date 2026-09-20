using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Switching a module off from the canvas: Ctrl+B, what it takes with it, and
/// what it refuses.
/// </summary>
/// <remarks>
/// What being off means to a patch is the compiler's, and is pinned there. Here
/// is the gesture: one press for a whole selection, back on again where all of
/// it is off, and the Output left alone since the graph has nowhere to put a
/// patch without one.
/// </remarks>
public class SwitchOffTests : UiTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private static Patch Chain(out NodeInstance clock, out NodeInstance osc, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        clock = builder.Add("time", 0, 0);
        osc = builder.Add("osc.sine", 300, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 700, 0);

        builder.Wire(clock, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    private static (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = patch;
        Settle(window);

        return (editor, window);
    }

    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static void Click(
        NodeEditor editor,
        Window window,
        NodeInstance node,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(Body(node)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);

        Settle(window);
    }

    private static void Switch(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);
        Settle(window);
    }

    [AvaloniaFact]
    public void Ctrl_b_switches_the_selected_module_off_and_on_again()
    {
        var patch = Chain(out _, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, osc);
        Switch(window);

        osc.Off.ShouldBeTrue();

        Switch(window);

        osc.Off.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_whole_selection_goes_off_in_one_press()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, clock);
        Click(editor, window, osc, RawInputModifiers.Control);
        Switch(window);

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();
    }

    /// <summary>
    /// A selection with anything still on goes off rather than flipping module by
    /// module, so one press never leaves it half and half.
    /// </summary>
    [AvaloniaFact]
    public void A_selection_that_is_partly_off_goes_off_the_rest_of_the_way()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var (editor, window) = Editing(patch);

        clock.Off = true;

        Click(editor, window, clock);
        Click(editor, window, osc, RawInputModifiers.Control);
        Switch(window);

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_Output_is_never_switched_off()
    {
        var patch = Chain(out _, out _, out var sink);
        var (editor, window) = Editing(patch);

        Click(editor, window, sink);
        Switch(window);

        sink.Off.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Switching_off_is_one_step_to_undo()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var (editor, window) = Editing(patch);

        Click(editor, window, clock);
        Click(editor, window, osc, RawInputModifiers.Control);
        Switch(window);

        editor.Undo().ShouldBeTrue();

        editor.Patch.Find(clock.Id).ShouldNotBeNull().Off.ShouldBeFalse();
        editor.Patch.Find(osc.Id).ShouldNotBeNull().Off.ShouldBeFalse();
    }

    /// <summary>
    /// The power mark is a drawing rather than a character, and a drawing fails
    /// quietly: Avalonia renders nothing for a path it could not read, and an arc
    /// whose sweep resolves the other way lands outside its box.
    /// </summary>
    [AvaloniaFact]
    public void The_switch_glyph_is_a_shape_inside_its_own_box()
    {
        var path = Glyphs.Switch().ShouldBeOfType<Avalonia.Controls.Shapes.Path>();
        var bounds = path.Data.ShouldNotBeNull().GetRenderBounds(new Pen(Brushes.White, 1.2));

        bounds.Width.ShouldBeGreaterThan(6);
        bounds.Height.ShouldBeGreaterThan(6);
        bounds.X.ShouldBeGreaterThanOrEqualTo(0);
        bounds.Y.ShouldBeGreaterThanOrEqualTo(0);
        bounds.Right.ShouldBeLessThanOrEqualTo(16);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(16);
    }

    // --- the panel's button --------------------------------------------------

    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        All<NodeEditor>(window).Single().Patch = patch;
        Settle(window);

        return window;
    }

    private static void ClickOn(MainWindow window, NodeInstance node)
    {
        var editor = All<NodeEditor>(window).Single();

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(Body(node)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static string Tip(MainWindow window) =>
        ToolTip.GetTip(All<Button>(window).First(b => b.Name == "switch-modules")) as string ?? string.Empty;

    /// <summary>
    /// The panel's buttons are glyphs, so what one does is read off its tip —
    /// and this one has to say which way it goes, which means the panel is
    /// rebuilt on an edit that changes no selection and no wire.
    /// </summary>
    [AvaloniaFact]
    public void The_panels_button_says_which_way_it_goes()
    {
        var patch = Chain(out _, out var osc, out _);
        var window = Open(patch);

        ClickOn(window, osc);

        Tip(window).ShouldContain("Switch this module off");

        Switch(window);

        Tip(window).ShouldContain("back on");
        osc.Off.ShouldBeTrue();
    }

    /// <summary>The Output has no such button: it is never switched off.</summary>
    [AvaloniaFact]
    public void The_panel_offers_the_Output_nothing_to_switch()
    {
        var patch = Chain(out _, out _, out var sink);
        var window = Open(patch);

        ClickOn(window, sink);

        All<Button>(window).Select(b => b.Name).ShouldNotContain("switch-modules");
    }
}
