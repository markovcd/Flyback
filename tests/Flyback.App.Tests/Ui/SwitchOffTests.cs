using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    private static Patch Chain(out NodeInstance clock, out NodeInstance osc, out NodeInstance sink)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        clock = builder.Add("time", 0, 0);
        osc = builder.Add("osc.sine", 300, 0);
        sink = builder.Add(NodeCatalog.OutputTypeId, 700, 0);

        builder.Wire(clock, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
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

        editor.History.Undo().ShouldBeTrue();

        editor.History.Patch.Find(clock.Id).ShouldNotBeNull().Off.ShouldBeFalse();
        editor.History.Patch.Find(osc.Id).ShouldNotBeNull().Off.ShouldBeFalse();
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

    private static void ClickOn(MainWindow window, NodeInstance node)
    {
        var editor = All<NodeEditor>(window).Single();

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(Body(node)), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static string Tip(MainWindow window, string name = "switch-modules") =>
        ToolTip.GetTip(All<Button>(window).First(b => b.Name == name)) as string ?? string.Empty;

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

    /// <summary>
    /// The panel matches the canvas for a module switched off (ADR-0117): the
    /// wash fades and the name is struck through, and both come back the moment
    /// the module is switched back on.
    /// </summary>
    [AvaloniaFact]
    public void The_panel_fades_and_strikes_a_switched_off_module()
    {
        var patch = Chain(out _, out var osc, out _);
        var window = Open(patch);

        ClickOn(window, osc);

        // Read afresh each time: the panel rebuilds a new plate and a new title
        // on every toggle, the same as a fresh selection does.
        var wash = All<ModuleWash>(window).Single();
        TextBlock Title() => All<ModulePlate>(window).Single().Named.Children[0].ShouldBeOfType<TextBlock>();

        wash.Off.ShouldBeFalse();
        Title().TextDecorations.ShouldBeNull();

        Switch(window);

        wash.Off.ShouldBeTrue();
        Title().TextDecorations.ShouldNotBeNull();

        Switch(window);

        wash.Off.ShouldBeFalse();
        Title().TextDecorations.ShouldBeNull();
    }

    // --- a box ---------------------------------------------------------------

    private static NodeGroup Boxed(Patch patch, params NodeInstance[] members) =>
        patch.Group(members.Select(m => m.Id))
        ?? throw new InvalidOperationException("the graph refused these modules a box");

    private static void ClickBox(NodeEditor editor, Window window, NodeGroup group)
    {
        var patch = editor.History.Patch;
        var bounds = NodeGeometry.GroupBounds(patch, group, patch.SocketsOf(group));
        var header = new Point(bounds.Center.X, bounds.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(header), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    private static void Press(MainWindow window, string name)
    {
        var button = All<Button>(window).First(b => b.Name == name);

        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the button is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    /// <summary>
    /// A box takes the key the same way a module does, because pressing one
    /// selects the modules in it and the selection is what a press switches.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_b_switches_every_module_in_a_box()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = Boxed(patch, clock, osc);
        var (editor, window) = Editing(patch);

        ClickBox(editor, window, group);
        Switch(window);

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();
        editor.Selection.Scene.SwitchedOff(group).ShouldBeTrue();

        Switch(window);

        clock.Off.ShouldBeFalse();
        osc.Off.ShouldBeFalse();
        editor.Selection.Scene.SwitchedOff(group).ShouldBeFalse();
    }

    /// <summary>
    /// The box is off only where all of it is. One module still on is a group
    /// that still does something, and a box drawn off would say otherwise.
    /// </summary>
    [AvaloniaFact]
    public void A_box_with_one_module_still_on_is_not_off()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = Boxed(patch, clock, osc);
        var (editor, _) = Editing(patch);

        clock.Off = true;

        editor.Selection.Scene.SwitchedOff(group).ShouldBeFalse();
    }

    /// <summary>
    /// The group's panel carries the same button the module's does, since a
    /// selection that is exactly a group gets a panel of its own and would
    /// otherwise be the one selection with nothing to press.
    /// </summary>
    [AvaloniaFact]
    public void The_box_panel_switches_what_is_in_it()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = Boxed(patch, clock, osc);
        var window = Open(patch);

        ClickBox(All<NodeEditor>(window).Single(), window, group);

        Tip(window, "switch-group").ShouldContain("2 modules in this box off");

        Press(window, "switch-group");

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();

        Tip(window, "switch-group").ShouldContain("back on");
    }
}
