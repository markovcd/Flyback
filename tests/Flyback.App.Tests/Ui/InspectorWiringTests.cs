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
/// A wire arriving at the module the inspector is showing takes that input's
/// knob away, and unplugging it gives the knob back — both without the module
/// having to be selected again.
/// </summary>
/// <remarks>
/// Patching is not a selection change, and the panel is rebuilt from nothing
/// each time the selection moves. So a knob went on standing under a socket that
/// had just been wired, showing a number the patch had stopped reading, until
/// something else was clicked and it silently turned into "patched". What the
/// panel now compares is which rows it has rather than what is in them, which is
/// also what keeps a slider being dragged from being torn down under the hand
/// holding it — the last test here is that half.
/// </remarks>
public class InspectorWiringTests : UiTest
{
    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = patch;
        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    /// <summary>
    /// An oscillator, a clock beside it to patch one of its inputs from, and the
    /// Output the oscillator already feeds.
    /// </summary>
    /// <param name="wired">
    /// Whether the clock is already on the oscillator's first input. The panel
    /// is then correct the moment it is built, which is what the unplugging test
    /// needs: starting from a panel that is already stale would let it pass
    /// against the very bug it is here for.
    /// </param>
    private static (Patch Patch, NodeInstance Sine, NodeInstance Clock) Board(bool wired = false)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var sine = b.Add("osc.sine", 360, 40);
        var clock = b.Add("time", 40, 40);

        b.Wire(sine, 0, output, NodeCatalog.OutputColorPort);
        if (wired) b.Wire(clock, 0, sine, 0);

        return (b.Patch, sine, clock);
    }

    /// <summary>A point in graph space, in the window's own coordinates.</summary>
    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    /// <summary>The middle of a module's header, which is where a click selects it.</summary>
    private static Point Body(NodeInstance node) => new(
        node.X + NodeGeometry.Width / 2,
        node.Y + NodeGeometry.HeaderHeight / 2);

    private static void Select(MainWindow window, NodeInstance node)
    {
        var at = OnWindow(window, Body(node));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static void DragFrom(MainWindow window, Point fromGraph, Point toGraph)
    {
        window.MouseDown(OnWindow(window, fromGraph), MouseButton.Left);
        window.MouseMove(OnWindow(window, toGraph));
        window.MouseUp(OnWindow(window, toGraph), MouseButton.Left);
        Settle(window);
    }

    /// <summary>Every knob on the panel. The inspector owns the only sliders the shell builds.</summary>
    private static int Knobs(MainWindow window) => All<Slider>(window).Count();

    /// <summary>How many rows say the socket is wired rather than offering a knob.</summary>
    private static int Wired(MainWindow window) =>
        All<TextBlock>(window).Count(t => t.Text?.Contains("patched") == true);

    private static Point Input(NodeInstance node, int index) =>
        NodeGeometry.InputPort(node, NodeCatalog.BuiltIn.Require(node.TypeId), index);

    /// <summary>Five inputs on a Sine, none of them wired to begin with.</summary>
    private const int SineInputs = 5;

    [AvaloniaFact]
    public void A_wire_arriving_takes_the_knob_away_without_reselecting()
    {
        var (patch, sine, clock) = Board();
        var window = Open(patch);

        Select(window, sine);

        Knobs(window).ShouldBe(SineInputs);
        Wired(window).ShouldBe(0);

        DragFrom(window, NodeGeometry.OutputPort(clock, 0), Input(sine, 0));

        // Nothing has been selected in between: this is the same panel, asked
        // again because the patch changed under it.
        Editor(window).Patch.IncomingTo(sine.Id, 0).ShouldNotBeNull();
        Knobs(window).ShouldBe(SineInputs - 1);
        Wired(window).ShouldBe(1);
    }

    [AvaloniaFact]
    public void Unplugging_gives_the_knob_back_without_reselecting()
    {
        var (patch, sine, _) = Board(wired: true);
        var window = Open(patch);

        Select(window, sine);

        Knobs(window).ShouldBe(SineInputs - 1);
        Wired(window).ShouldBe(1);

        // Grabbing a wired input picks the wire up by its far end; dropped on a
        // module's body it is a miss, so the wire is simply gone. Bare canvas
        // would open the module list instead, which is a different gesture.
        DragFrom(window, Input(sine, 0), Body(sine));

        Editor(window).Patch.IncomingTo(sine.Id, 0).ShouldBeNull();
        Knobs(window).ShouldBe(SineInputs);
        Wired(window).ShouldBe(0);
    }

    /// <summary>
    /// The other half of the rule: a knob turned is a value and not a row, so
    /// the panel is left alone. Were it rebuilt on every patch change instead,
    /// the slider under the pointer would be replaced mid-drag and the gesture
    /// would end after one frame of it.
    /// </summary>
    [AvaloniaFact]
    public void Turning_a_knob_leaves_the_panel_standing()
    {
        var (patch, sine, _) = Board();
        var window = Open(patch);

        Select(window, sine);

        var knob = All<Slider>(window).First();
        var was = knob.Value;

        knob.Value = was + 0.25;
        Settle(window);

        All<Slider>(window).ShouldContain(knob);
        sine.InputValues[0].ShouldBe((float)(was + 0.25), 0.001f);
    }
}
