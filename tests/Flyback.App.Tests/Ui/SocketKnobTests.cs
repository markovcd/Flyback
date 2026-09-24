using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Holding the right button on an unpatched input and dragging
/// up and down turns that input's value.
/// </summary>
public class SocketKnobTests : UiTest
{
    private static Patch Chain(out NodeInstance clock, out NodeInstance osc)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        clock = builder.Add("time", 0, 0);
        osc = builder.Add("osc.sine", 300, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 700, 0);

        builder.Wire(clock, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    /// <summary>A canvas whose turns leave the pointer free, since there is no real pointer to hold.</summary>
    private (NodeEditor Editor, Window Window) Editing(Patch patch) =>
        Editing(patch, services => services.AddSingleton<Func<Visual, IPointerAnchor?>>(_ => (IPointerAnchor?)null));

    /// <summary>The first input on <paramref name="node"/> that has a knob of its own.</summary>
    private static int Turnable(Patch patch, NodeInstance node)
    {
        var def = NodeCatalog.Get(node.TypeId).ShouldNotBeNull();

        return Enumerable.Range(0, def.Inputs.Count)
            .First(i => KnobLinking.Linkable(def.Inputs[i]) && patch.IncomingTo(node.Id, i) is null);
    }

    private static Point Socket(NodeInstance node, int port) =>
        Geometry.InputPort(node, NodeCatalog.Get(node.TypeId)!, port);

    private static Point On(NodeEditor editor, Window window, Point graph) =>
        editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
        ?? throw new InvalidOperationException("the editor is not in this window");

    private static void Turn(Window window, Point from, double up, MouseButton button = MouseButton.Right)
    {
        window.MouseDown(from, button);
        window.MouseMove(from - new Point(0, up));
        window.MouseUp(from - new Point(0, up), button);
        Settle(window);
    }

    [AvaloniaFact]
    public void Dragging_up_on_an_unpatched_input_raises_its_value()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];

        Turn(window, On(editor, window, Socket(osc, port)), 40);

        osc.InputValues[port].ShouldBeGreaterThan(was);
    }

    [AvaloniaFact]
    public void Dragging_down_lowers_it()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];

        Turn(window, On(editor, window, Socket(osc, port)), -40);

        osc.InputValues[port].ShouldBeLessThan(was);
    }

    [AvaloniaFact]
    public void A_whole_turn_is_one_step_to_undo()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];
        var from = On(editor, window, Socket(osc, port));

        window.MouseDown(from, MouseButton.Right);
        for (var i = 1; i <= 5; i++) window.MouseMove(from - new Point(0, i * 8));
        window.MouseUp(from - new Point(0, 40), MouseButton.Right);
        Settle(window);

        editor.History.Undo().ShouldBeTrue();

        editor.History.Patch.Find(osc.Id)!.InputValues[port].ShouldBe(was);
        editor.History.Undo().ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Escape_puts_the_value_back()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];
        var from = On(editor, window, Socket(osc, port));

        window.MouseDown(from, MouseButton.Right);
        window.MouseMove(from - new Point(0, 40));
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.MouseUp(from - new Point(0, 40), MouseButton.Right);
        Settle(window);

        osc.InputValues[port].ShouldBe(was);
    }

    [AvaloniaFact]
    public void A_patched_input_is_not_turned()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var was = osc.InputValues[0];

        Turn(window, On(editor, window, Socket(osc, 0)), 40);

        osc.InputValues[0].ShouldBe(was);
    }

    [AvaloniaFact]
    public void The_hand_coming_off_is_announced_once()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);

        var turned = 0;
        var letGo = new List<SocketPick>();
        editor.Dial.InputTurned += (_, _) => turned++;
        editor.Dial.InputLetGo += (_, pick) => letGo.Add(pick);

        Turn(window, On(editor, window, Socket(osc, port)), 40);

        turned.ShouldBeGreaterThan(0);
        letGo.ShouldBe([new SocketPick(osc.Id, port)]);
    }

    [AvaloniaFact]
    public void The_left_button_on_an_input_still_draws_a_wire_rather_than_turning()
    {
        var patch = Chain(out _, out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];

        Turn(window, On(editor, window, Socket(osc, port)), 40, MouseButton.Left);

        osc.InputValues[port].ShouldBe(was);
    }
}
