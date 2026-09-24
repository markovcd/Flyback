using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Holding the right button on a module, or on a shut box, switches it off until
/// the button comes up.
/// </summary>
public class HoldMuteTests : UiTest
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

    private (NodeEditor Editor, Window Window) Editing(Patch patch)
    {
        var editor = NewCanvas(Wide, Tall);
        var window = Show(editor, Wide);

        editor.History.Open(patch);
        Settle(window);

        return (editor, window);
    }

    private static Point On(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static Point Body(NodeInstance node) =>
        new(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

    private static Point Header(NodeEditor editor, NodeGroup group)
    {
        var patch = editor.History.Patch;
        var bounds = NodeGeometry.GroupBounds(patch, group, patch.SocketsOf(group));

        return new Point(bounds.Center.X, bounds.Y + NodeGeometry.HeaderHeight / 2);
    }

    private static void Down(NodeEditor editor, Window window, Point graph)
    {
        window.MouseDown(On(editor, window, graph), MouseButton.Right);
        Settle(window);
    }

    private static void Up(NodeEditor editor, Window window, Point graph)
    {
        window.MouseUp(On(editor, window, graph), MouseButton.Right);
        Settle(window);
    }

    [AvaloniaFact]
    public void A_module_is_off_while_the_button_is_down_and_on_when_it_comes_up()
    {
        var (editor, window) = Editing(Chain(out _, out var osc, out _));

        Down(editor, window, Body(osc));

        osc.Off.ShouldBeTrue();

        Up(editor, window, Body(osc));

        osc.Off.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Only_the_module_under_the_pointer_is_muted_of_a_selection()
    {
        var (editor, window) = Editing(Chain(out var clock, out var osc, out _));

        editor.Selection.SelectAll();
        Down(editor, window, Body(osc));

        osc.Off.ShouldBeTrue();
        clock.Off.ShouldBeFalse();

        Up(editor, window, Body(osc));
    }

    [AvaloniaFact]
    public void A_module_that_is_off_is_on_while_the_button_is_down_and_off_again_after()
    {
        var (editor, window) = Editing(Chain(out _, out var osc, out _));

        osc.Off = true;

        Down(editor, window, Body(osc));

        osc.Off.ShouldBeFalse();

        Up(editor, window, Body(osc));

        osc.Off.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_box_that_is_entirely_off_is_on_while_held()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = patch.Group([clock.Id, osc.Id]).ShouldNotBeNull();
        var (editor, window) = Editing(patch);

        clock.Off = true;
        osc.Off = true;

        Down(editor, window, Header(editor, group));

        clock.Off.ShouldBeFalse();
        osc.Off.ShouldBeFalse();

        Up(editor, window, Header(editor, group));

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_Output_is_never_muted()
    {
        var (editor, window) = Editing(Chain(out _, out _, out var sink));

        Down(editor, window, Body(sink));

        sink.Off.ShouldBeFalse();

        Up(editor, window, Body(sink));
    }

    [AvaloniaFact]
    public void Holding_asks_for_a_new_sound_and_leaves_no_step_to_undo()
    {
        var (editor, window) = Editing(Chain(out _, out var osc, out _));

        var changes = 0;
        editor.History.PatchChanged += (_, _) => changes++;

        Down(editor, window, Body(osc));
        Up(editor, window, Body(osc));

        changes.ShouldBe(2);
        editor.History.Undo().ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_locked_canvas_is_not_muted_by_a_press()
    {
        var (editor, window) = Editing(Chain(out _, out var osc, out _));

        editor.History.Locked = true;
        Down(editor, window, Body(osc));

        osc.Off.ShouldBeFalse();

        Up(editor, window, Body(osc));
    }

    [AvaloniaFact]
    public void Holding_does_not_select_the_module()
    {
        var (editor, window) = Editing(Chain(out var clock, out var osc, out _));

        editor.Selection.Select(clock.Id);
        Down(editor, window, Body(osc));

        editor.Selection.Nodes.Select(n => n.Id).ShouldBe([clock.Id]);

        Up(editor, window, Body(osc));
    }

    [AvaloniaFact]
    public void The_left_button_does_not_mute()
    {
        var (editor, window) = Editing(Chain(out _, out var osc, out _));

        window.MouseDown(On(editor, window, Body(osc)), MouseButton.Left);

        osc.Off.ShouldBeFalse();

        window.MouseUp(On(editor, window, Body(osc)), MouseButton.Left);
    }

    [AvaloniaFact]
    public void A_shut_box_mutes_every_module_in_it_while_held()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = patch.Group([clock.Id, osc.Id]).ShouldNotBeNull();
        var (editor, window) = Editing(patch);

        osc.Off = true;

        Down(editor, window, Header(editor, group));

        clock.Off.ShouldBeTrue();
        osc.Off.ShouldBeTrue();

        Up(editor, window, Header(editor, group));

        clock.Off.ShouldBeFalse();
        osc.Off.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void In_an_open_group_only_the_module_pressed_is_muted()
    {
        var patch = Chain(out var clock, out var osc, out _);
        var group = patch.Group([clock.Id, osc.Id]).ShouldNotBeNull();
        var (editor, window) = Editing(patch);

        editor.Edits.ToggleBox(group);
        Settle(window);
        group.Collapsed.ShouldBeFalse();

        Down(editor, window, Body(osc));

        osc.Off.ShouldBeTrue();
        clock.Off.ShouldBeFalse();

        Up(editor, window, Body(osc));

        osc.Off.ShouldBeFalse();
    }
}
