using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// The well an unpatched input's value sits in: dragging it with the left button turns
/// the value, Alt-click puts it back to its default, and it lights under the pointer.
/// </summary>
public class ValueWellTests : EditorTest
{
    private static Patch Chain(out NodeInstance osc)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var clock = builder.Add("time", 0, 0);
        osc = builder.Add("osc.sine", 300, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 700, 0);

        builder.Wire(clock, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    /// <summary>A canvas whose turns leave the pointer free, since there is no real pointer to hold.</summary>
    private (NodeEditor Editor, Window Window) Editing(Patch patch) =>
        Editing(patch, services => services.AddSingleton<IPointerAnchors>(new Free()));

    private sealed class Free : IPointerAnchors
    {
        public IPointerAnchor? Take(Visual visual) => null;
    }

    private static int Turnable(Patch patch, NodeInstance node)
    {
        var def = NodeCatalog.Get(node.TypeId).ShouldNotBeNull();

        return Enumerable.Range(0, def.Inputs.Count).First(i => ValueWell.Has(patch, node, def, i));
    }

    private static Point Well(NodeEditor editor, Window window, NodeInstance node, int port)
    {
        var def = NodeCatalog.Get(node.TypeId)!;
        var graph = ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    private static void Drag(Window window, Point from, double up, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(from, MouseButton.Left, modifiers);
        for (var i = 1; i <= 4; i++) window.MouseMove(from - new Point(0, up * i / 4), modifiers);
        window.MouseUp(from - new Point(0, up), MouseButton.Left, modifiers);
        Settle(window);
    }

    [AvaloniaFact]
    public void Dragging_a_well_up_with_the_left_button_raises_its_value()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];

        Drag(window, Well(editor, window, osc, port), 40);

        osc.InputValues[port].ShouldBeGreaterThan(was);
    }

    [AvaloniaFact]
    public void Turning_a_well_leaves_the_module_where_it_was_and_selects_it()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var at = (osc.X, osc.Y);

        Drag(window, Well(editor, window, osc, Turnable(patch, osc)), 40);

        (osc.X, osc.Y).ShouldBe(at);
        editor.Selection.Contains(osc.Id).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_turn_of_a_well_is_one_step_to_undo()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];

        Drag(window, Well(editor, window, osc, port), 40);

        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Find(osc.Id)!.InputValues[port].ShouldBe(was);
    }

    [AvaloniaFact]
    public void Alt_click_on_a_well_puts_the_value_back_to_its_default()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var spec = NodeCatalog.Get(osc.TypeId)!.Inputs[port];
        osc.InputValues[port] = spec.Default + 1;
        var at = Well(editor, window, osc, port);

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Alt);
        Settle(window);

        editor.History.Patch.Find(osc.Id)!.InputValues[port].ShouldBe(spec.Default);
    }

    [AvaloniaFact]
    public void A_well_lights_under_the_pointer_and_goes_out_when_it_leaves()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var at = Well(editor, window, osc, port);

        window.MouseMove(at);
        Settle(window);
        editor.WellFocus.StateOf(osc.Id, port).ShouldBe(WellState.Hover);

        window.MouseMove(at + new Point(0, 400));
        Settle(window);
        editor.WellFocus.StateOf(osc.Id, port).ShouldBe(WellState.Rest);
    }

    [AvaloniaFact]
    public void A_well_shows_turning_while_held_and_fine_with_Shift()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var at = Well(editor, window, osc, port);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at - new Point(0, 10));
        editor.WellFocus.StateOf(osc.Id, port).ShouldBe(WellState.Turn);

        window.MouseMove(at - new Point(0, 20), RawInputModifiers.Shift);
        editor.WellFocus.StateOf(osc.Id, port).ShouldBe(WellState.Fine);

        window.MouseUp(at - new Point(0, 20), MouseButton.Left);
        Settle(window);
        editor.Dial.Turning.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_patched_input_has_no_well()
    {
        var patch = Chain(out var osc);
        var def = NodeCatalog.Get(osc.TypeId)!;

        ValueWell.Has(patch, osc, def, 0).ShouldBeFalse("the clock is wired into it");
    }

    [AvaloniaFact]
    public void A_locked_canvas_does_not_turn_a_well()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var was = osc.InputValues[port];
        editor.History.Locked = true;

        Drag(window, Well(editor, window, osc, port), 40);

        osc.InputValues[port].ShouldBe(was);
    }

    [AvaloniaFact]
    public void A_compact_module_has_no_wells_to_turn()
    {
        var patch = Chain(out var osc);
        var (editor, window) = Editing(patch);
        var port = Turnable(patch, osc);
        var at = Well(editor, window, osc, port);
        var was = osc.InputValues[port];
        editor.Geometry.Compact = true;

        Drag(window, at, 40);

        osc.InputValues[port].ShouldBe(was);
    }
}
