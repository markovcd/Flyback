using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// A clicked well takes the keys: arrows nudge it, Home and End send it to the ends of
/// its range, Backspace resets it, Tab moves on, the wheel nudges it, and Enter or a
/// double-click opens it for an exact value.
/// </summary>
public class SocketFocusTests : EditorTest
{
    private sealed class Free : IPointerAnchors
    {
        public IPointerAnchor? Take(Visual visual) => null;
    }

    private (NodeEditor Editor, Window Window, NodeInstance Sine, SocketPick Freq) Clicked()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, 200);
        var freq = new SocketPick(sine.Id, Port(sine, "freq"));

        var (editor, window) = Editing(builder.Patch, services => services.AddSingleton<IPointerAnchors>(new Free()));
        var at = Well(editor, window, sine, freq.Port);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        return (editor, window, sine, freq);
    }

    private static int Port(NodeInstance node, string name) =>
        NodeCatalog.Get(node.TypeId)!.Inputs.ToList().FindIndex(port => port.Name == name);

    private static PortSpec Spec(NodeInstance node, int port) => NodeCatalog.Get(node.TypeId)!.Inputs[port];

    private static Point Well(NodeEditor editor, Window window, NodeInstance node, int port)
    {
        var def = NodeCatalog.Get(node.TypeId)!;
        var graph = ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)!.Value;
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Settle(window);
    }

    private static void Type(Window window, string text)
    {
        window.KeyTextInput(text);
        Settle(window);
    }

    [AvaloniaFact]
    public void A_click_on_a_well_gives_it_the_keys_and_shows_its_readout()
    {
        var (editor, window, sine, freq) = Clicked();

        editor.WellFocus.Focused.ShouldBe(freq);
        editor.WellFocus.StateOf(sine.Id, freq.Port).ShouldBe(WellState.Focus);
        editor.WellFocus.Reading.ShouldNotBeNull().Pick.ShouldBe(freq);
        Shoot(window, "well-focused.png");
    }

    [AvaloniaFact]
    public void Up_raises_the_value_and_Shift_Up_raises_it_less()
    {
        var (_, window, sine, freq) = Clicked();
        var was = sine.InputValues[freq.Port];

        Press(window, Key.Up);
        var step = sine.InputValues[freq.Port] - was;
        step.ShouldBeGreaterThan(0);

        var now = sine.InputValues[freq.Port];
        Press(window, Key.Up, RawInputModifiers.Shift);
        (sine.InputValues[freq.Port] - now).ShouldBeInRange(float.Epsilon, step);
    }

    [AvaloniaFact]
    public void Home_and_End_send_the_value_to_the_ends_of_its_range()
    {
        var (_, window, sine, freq) = Clicked();
        var spec = Spec(sine, freq.Port);

        Press(window, Key.End);
        sine.InputValues[freq.Port].ShouldBe(spec.Max);

        Press(window, Key.Home);
        sine.InputValues[freq.Port].ShouldBe(spec.Min);
    }

    [AvaloniaFact]
    public void Backspace_puts_the_default_back_rather_than_deleting_the_module()
    {
        var (editor, window, sine, freq) = Clicked();
        Press(window, Key.Up);

        Press(window, Key.Back);

        sine.InputValues[freq.Port].ShouldBe(Spec(sine, freq.Port).Default);
        editor.History.Patch.Find(sine.Id).ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Enter_opens_the_well_and_a_typed_value_is_set_on_Enter_as_one_undo_step()
    {
        var (editor, window, sine, freq) = Clicked();
        var was = sine.InputValues[freq.Port];

        Press(window, Key.Enter);
        editor.WellFocus.Typed.ShouldBe(string.Empty);

        Type(window, "0.9");
        sine.InputValues[freq.Port].ShouldBe(0.9f, "the patch follows what is typed as it is typed");

        Press(window, Key.Enter);
        editor.WellFocus.Typed.ShouldBeNull();
        editor.WellFocus.Focused.ShouldBe(freq, "the well keeps the keys");

        editor.History.Undo().ShouldBeTrue();
        editor.History.Patch.Find(sine.Id)!.InputValues[freq.Port].ShouldBe(was);
    }

    [AvaloniaFact]
    public void Escape_while_typing_puts_the_value_back()
    {
        var (editor, window, sine, freq) = Clicked();
        var was = sine.InputValues[freq.Port];

        Press(window, Key.Enter);
        Type(window, "3");
        Press(window, Key.Escape);

        sine.InputValues[freq.Port].ShouldBe(was);
        editor.WellFocus.Typed.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Text_that_reads_as_nothing_keeps_the_value_and_the_box_open()
    {
        var (editor, window, sine, freq) = Clicked();
        var was = sine.InputValues[freq.Port];

        Press(window, Key.Enter);
        Type(window, "0..8x");
        Press(window, Key.Enter);

        sine.InputValues[freq.Port].ShouldBe(was);
        editor.WellFocus.Typed.ShouldBe("0..8x");
        Shoot(window, "typing-unread.png");
        TurnReadout.Verdict(editor.WellFocus.Reading!.Value.Turn)!.Value.Tone.ShouldBe(TurnReadout.Tone.Error);
    }

    [AvaloniaFact]
    public void A_value_past_the_range_is_taken_and_widens_it()
    {
        var (editor, window, sine, freq) = Clicked();
        var past = Spec(sine, freq.Port).Max + 10;

        Press(window, Key.Enter);
        Type(window, past.ToString(System.Globalization.CultureInfo.InvariantCulture));
        TurnReadout.Verdict(editor.WellFocus.Reading!.Value.Turn)!.Value.Tone.ShouldBe(TurnReadout.Tone.Warning);
        Shoot(window, "typing-past-range.png");
        Press(window, Key.Enter);

        sine.InputValues[freq.Port].ShouldBe(past);
        editor.WellFocus.Reading!.Value.Turn.Max.ShouldBe(past);
    }

    [AvaloniaFact]
    public void A_double_click_on_a_well_opens_it_for_typing()
    {
        var (editor, window, sine, _) = Clicked();
        var amp = Port(sine, "amp");
        var at = Well(editor, window, sine, amp);

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        editor.WellFocus.Focused.ShouldBe(new SocketPick(sine.Id, amp));
        editor.WellFocus.Typed.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void Tab_moves_the_keys_to_the_next_well_on_the_module()
    {
        var (editor, window, sine, freq) = Clicked();

        Press(window, Key.Tab);

        var next = editor.WellFocus.Focused.ShouldNotBeNull();
        next.Node.ShouldBe(sine.Id);
        next.Port.ShouldNotBe(freq.Port);
        ValueWell.Has(editor.History.Patch, sine, NodeCatalog.Get(sine.TypeId)!, next.Port).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_wheel_nudges_the_well_with_the_keys_and_zooms_everywhere_else()
    {
        var (editor, window, sine, freq) = Clicked();
        var was = sine.InputValues[freq.Port];
        var zoom = editor.View.Zoom;

        window.MouseWheel(Well(editor, window, sine, freq.Port), new Vector(0, 1));
        Settle(window);

        sine.InputValues[freq.Port].ShouldBeGreaterThan(was);
        editor.View.Zoom.ShouldBe(zoom);

        window.MouseWheel(Well(editor, window, sine, Port(sine, "amp")), new Vector(0, 1));
        Settle(window);

        editor.View.Zoom.ShouldNotBe(zoom, "a well without the keys leaves the wheel to the zoom");
    }

    [AvaloniaFact]
    public void A_press_elsewhere_takes_the_keys_back()
    {
        var (editor, window, _, _) = Clicked();

        window.MouseDown(new Point(20, 20), MouseButton.Left);
        window.MouseUp(new Point(20, 20), MouseButton.Left);
        Settle(window);

        editor.WellFocus.Focused.ShouldBeNull();
        editor.WellFocus.Reading.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Escape_takes_the_keys_back()
    {
        var (editor, window, _, _) = Clicked();

        Press(window, Key.Escape);

        editor.WellFocus.Focused.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_typed_value_is_announced_once_it_is_applied()
    {
        var (editor, window, sine, freq) = Clicked();
        var letGo = new List<SocketPick>();
        editor.Reactions.Add<InputLetGo>(notice => letGo.Add(notice.Pick));

        Press(window, Key.Enter);
        Type(window, "1");
        Press(window, Key.Enter);

        sine.InputValues[freq.Port].ShouldBe(1f);
        letGo.ShouldBe([freq]);
    }
}
