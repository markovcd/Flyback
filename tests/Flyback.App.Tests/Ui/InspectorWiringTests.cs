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
/// A wire arriving at the module the inspector is showing takes that input's knob
/// away, and unplugging it gives the knob back — both without the module having to
/// be selected again.
/// </summary>
/// <remarks>
/// Patching is not a selection change, so the panel compares which rows it has
/// rather than what is in them. The same check keeps a slider being dragged from
/// being torn down under the hand holding it, which is the last test here.
/// </remarks>
public class InspectorWiringTests : UiTest
{

    /// <summary>
    /// An oscillator, a clock beside it to patch one of its inputs from, and the
    /// Output the oscillator already feeds.
    /// </summary>
    /// <param name="wired">
    /// Whether the clock is already on the oscillator's 'freq', so the panel is
    /// correct the moment it is built — starting from a stale panel would let the
    /// unplugging test pass against the very bug it is here for. 'freq' rather than
    /// 'in' because 'in' has no knob to come or go, being normalled to Time.
    /// </param>
    private static (Patch Patch, NodeInstance Sine, NodeInstance Clock) Board(bool wired = false)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var sine = b.Add("osc.sine", 360, 40);
        var clock = b.Add("time", 40, 40);

        b.Wire(sine, 0, output, NodeCatalog.OutputColorPort);
        if (wired) b.Wire(clock, 0, sine, 1);

        return (b.Patch, sine, clock);
    }

    /// <summary>A point in graph space, in the window's own coordinates.</summary>
    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

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
    private static int KnobCount(MainWindow window) => Knobs(window).Count();

    /// <summary>How many rows say the socket is wired rather than offering a knob.</summary>
    private static int Wired(MainWindow window) =>
        All<TextBlock>(window).Count(t => t.Text?.Contains("◀ patched") == true);

    /// <summary>How many rows say the socket is driven with no wire to show for it.</summary>
    private static int Normalled(MainWindow window) =>
        All<TextBlock>(window).Count(t => t.Text?.Contains("◀ Time, without a wire") == true);

    /// <summary>How many rows name a specific socket as what drives another without a wire.</summary>
    private static int NormalledFrom(MainWindow window, string socket) =>
        All<TextBlock>(window).Count(t => t.Text == $"◀ {socket}, without a wire");

    /// <summary>How many rows say the socket has nothing worth a knob, and no wire either.</summary>
    private static int NotPatched(MainWindow window) =>
        All<TextBlock>(window).Count(t => t.Text == "◀ not patched");

    private static Point Input(NodeInstance node, int index) =>
        Geometry.InputPort(node, NodeCatalog.BuiltIn.Require(node.TypeId), index);

    /// <summary>
    /// How many knobs a Sine offers with nothing wired into it. Five inputs, and
    /// four of them: 'in' is normalled to Time, and a normalled socket has no
    /// knob because nothing would read the value one was turned to.
    /// </summary>
    private const int SineKnobs = 4;

    [AvaloniaFact]
    public void A_wire_arriving_takes_the_knob_away_without_reselecting()
    {
        var (patch, sine, clock) = Board();
        var window = Open(patch);

        Select(window, sine);

        KnobCount(window).ShouldBe(SineKnobs);
        Wired(window).ShouldBe(0);

        // Onto 'freq', which is a knob. Wiring 'in' would take nothing away —
        // there was no knob under it to lose.
        DragFrom(window, NodeGeometry.OutputPort(clock, 0), Input(sine, 1));

        // Nothing has been selected in between: this is the same panel, asked
        // again because the patch changed under it.
        Editor(window).History.Patch.IncomingTo(sine.Id, 1).ShouldNotBeNull();
        KnobCount(window).ShouldBe(SineKnobs - 1);
        Wired(window).ShouldBe(1);
    }

    [AvaloniaFact]
    public void Unplugging_gives_the_knob_back_without_reselecting()
    {
        var (patch, sine, _) = Board(wired: true);
        var window = Open(patch);

        Select(window, sine);

        KnobCount(window).ShouldBe(SineKnobs - 1);
        Wired(window).ShouldBe(1);

        // Grabbing a wired input picks the wire up by its far end; dropped on a
        // module's body it is a miss, so the wire is simply gone. Bare canvas
        // would open the module list instead, which is a different gesture.
        DragFrom(window, Input(sine, 1), Body(sine));

        Editor(window).History.Patch.IncomingTo(sine.Id, 1).ShouldBeNull();
        KnobCount(window).ShouldBe(SineKnobs);
        Wired(window).ShouldBe(0);
    }

    /// <summary>A socket as the panel names the far end of a wire: <c>module.socket</c>.</summary>
    private static string End(NodeInstance node, int port, bool output)
    {
        var def = NodeCatalog.BuiltIn.Require(node.TypeId);

        return $"{node.Title(def)}.{(output ? def.Outputs : def.Inputs)[port].Name}";
    }

    private static bool Says(MainWindow window, string text) => All<TextBlock>(window).Any(t => t.Text == text);

    [AvaloniaFact]
    public void A_patched_input_names_the_socket_it_is_patched_from()
    {
        var (patch, sine, clock) = Board(wired: true);
        var window = Open(patch);

        Select(window, sine);

        Says(window, $"◀ patched from {End(clock, 0, output: true)}").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_patched_output_names_every_socket_it_feeds()
    {
        var (patch, sine, clock) = Board(wired: true);
        var window = Open(patch);
        var editor = Editor(window);

        editor.History.Patch.Connect(clock.Id, 0, sine.Id, 2);
        editor.History.Record();

        Select(window, clock);

        Says(window, $"▶ patched to {End(sine, 1, output: false)}, {End(sine, 2, output: false)}").ShouldBeTrue();
    }

    /// <summary>The far end's name is on the row, so renaming that module rewrites it.</summary>
    [AvaloniaFact]
    public void Renaming_the_far_module_rewrites_the_row()
    {
        var (patch, sine, clock) = Board(wired: true);
        var window = Open(patch);
        var editor = Editor(window);

        Select(window, sine);

        clock.Name = "beat";
        editor.History.Record();

        Says(window, $"◀ patched from beat.{End(clock, 0, output: true).Split('.')[1]}").ShouldBeTrue();
    }

    /// <summary>
    /// A socket that is driven without a wire says so where its knob would have
    /// been, and goes back to saying it when a wire that was there is pulled.
    /// </summary>
    [AvaloniaFact]
    public void A_normalled_socket_names_what_is_driving_it_instead_of_a_knob()
    {
        var (patch, sine, clock) = Board();
        var window = Open(patch);

        Select(window, sine);

        Normalled(window).ShouldBe(1);

        DragFrom(window, NodeGeometry.OutputPort(clock, 0), Input(sine, 0));

        Editor(window).History.Patch.IncomingTo(sine.Id, 0).ShouldNotBeNull();
        Normalled(window).ShouldBe(0);
        Wired(window).ShouldBe(1);

        DragFrom(window, Input(sine, 0), Body(sine));

        Editor(window).History.Patch.IncomingTo(sine.Id, 0).ShouldBeNull();
        Normalled(window).ShouldBe(1);
    }

    /// <summary>
    /// The Output's 'color' and 'left' have no fallback to name — an unwired
    /// color is a broadcast gray nobody chose, and 'left' fed a constant is a
    /// speaker humming rather than a setting — so they say plainly that
    /// nothing is patched instead of offering a slider that would mislead.
    /// 'right' falls back to 'left' and says so, the same as any other
    /// normalled socket. 'volume' is the one real dial, so it is the only
    /// knob the panel offers.
    /// </summary>
    [AvaloniaFact]
    public void The_output_offers_a_knob_for_volume_only()
    {
        var window = Open(Presets.Empty(NodeCatalog.BuiltIn));

        Select(window, Editor(window).History.Patch.Output);

        KnobCount(window).ShouldBe(1, "volume is the only socket worth dialing");
        NotPatched(window).ShouldBe(2, "color and left have nothing to fall back to");
        NormalledFrom(window, "left").ShouldBe(1, "right falls back to left");
    }

    /// <summary>
    /// Wiring 'left' takes away its "not patched" row exactly as wiring any
    /// other socket takes away its knob, and unplugging brings it back.
    /// </summary>
    [AvaloniaFact]
    public void Wiring_the_outputs_left_takes_away_its_not_patched_row()
    {
        var window = Open(Presets.Empty(NodeCatalog.BuiltIn));
        var editor = Editor(window);
        var output = editor.History.Patch.Output;

        var tone = editor.Edits.AddNode("osc.sine", new Point(600, 300));
        tone.ShouldNotBeNull();

        Select(window, output);
        NotPatched(window).ShouldBe(2);

        editor.History.Patch.Connect(tone.Id, 0, output.Id, NodeCatalog.OutputLeftPort);
        editor.History.Record();

        NotPatched(window).ShouldBe(1, "left is now patched");
        Wired(window).ShouldBe(1);

        editor.History.Patch.Disconnect(output.Id, NodeCatalog.OutputLeftPort);
        editor.History.Record();

        NotPatched(window).ShouldBe(2, "unplugging brings the row back");
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

        // The first knob on a Sine is 'freq': 'in' is above it and is normalled,
        // so it has a row and no slider in it.
        var knob = Knobs(window).First();
        var was = sine.InputValues[1];

        knob.Value += 0.25;
        Settle(window);

        Knobs(window).ShouldContain(knob);
        sine.InputValues[1].ShouldBeGreaterThan(was);
    }

    /// <summary>Lets go of the pointer over a control, which is what ends a gesture.</summary>
    private static void Release(Control over) =>
        over.RaiseEvent(new PointerReleasedEventArgs(
            over,
            new Pointer(0, PointerType.Mouse, true),
            over,
            default,
            0,
            default,
            KeyModifiers.None,
            MouseButton.Left)
        {
            RoutedEvent = InputElement.PointerReleasedEvent,
        });

    /// <summary>
    /// Two drags of one knob are two things somebody did, so one press takes back one
    /// of them.
    /// </summary>
    /// <remarks>
    /// The frames of a drag fold into one step under a name taken from the control,
    /// so every drag of this slider carries the same one. What tells them apart is
    /// the hand coming off between the two.
    /// </remarks>
    [AvaloniaFact]
    public void Two_drags_of_one_knob_come_back_one_at_a_time()
    {
        var (patch, sine, _) = Board();
        var window = Open(patch);

        Select(window, sine);

        var knob = Knobs(window).First();
        var was = sine.InputValues[1];
        var at = knob.Value;

        knob.Value = at + 0.25;
        Settle(window);
        Release(knob);
        Settle(window);

        var first = Editor(window).History.Patch.Find(sine.Id).ShouldNotBeNull().InputValues[1];

        knob.Value = at + 0.5;
        Settle(window);
        Release(knob);
        Settle(window);

        Editor(window).History.Undo().ShouldBeTrue();

        Editor(window).History.Patch.Find(sine.Id).ShouldNotBeNull()
            .InputValues[1].ShouldBe(first, 0.001f, "the second drag is what came back");

        Editor(window).History.Undo().ShouldBeTrue("and the first is still there to take back");

        Editor(window).History.Patch.Find(sine.Id).ShouldNotBeNull()
            .InputValues[1].ShouldBe(was, 0.001f);
    }

    /// <summary>
    /// A knob is a float and its number box holds a decimal, which stops near
    /// 7.9e28. A file may say any float, and selecting the module must not throw.
    /// </summary>
    [AvaloniaFact]
    public void A_module_with_a_knob_past_what_a_number_box_holds_can_be_selected()
    {
        var (patch, sine, _) = Board();

        for (var i = 0; i < sine.InputValues.Length; i++) sine.InputValues[i] = 1e30f;

        // Through the file format, which is how one arrives.
        var window = Open(PatchIO.Read(PatchIO.ToJson(patch)).Patch);
        var opened = Editor(window).History.Patch.Find(sine.Id).ShouldNotBeNull();

        Should.NotThrow(() => Select(window, opened));

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        opened.InputValues.ShouldAllBe(v => v == 1e30f, "showing a number must not change it");
        KnobCount(window).ShouldBeGreaterThan(0);
    }

    /// <summary>A number box emptied and left says the number in force.</summary>
    /// <remarks>
    /// An emptied box is no number, so the knob keeps the one it had, and a box
    /// showing nothing beside it would disagree. While it has the focus it is
    /// left alone: empty is what a box is on the way from one number to another.
    /// </remarks>
    [AvaloniaFact]
    public void An_emptied_number_box_in_the_inspector_says_what_is_in_force()
    {
        var (patch, sine, _) = Board();

        sine.InputValues[1] = 0.2f;

        var window = Open(patch);

        Select(window, sine);

        // The first box on a Sine is 'freq', beside the first knob.
        var box = All<NumericUpDown>(All<StackPanel>(window).Single(p => p.Name == "inspector")).First();

        box.Focus();
        box.Value = null;
        Settle(window);

        // Focus goes elsewhere, which is where a box that was being typed into
        // settles on what it says.
        Editor(window).Focus();
        Settle(window);

        sine.InputValues[1].ShouldBe(0.2f, "an empty box is not a number, and the knob keeps the one it had");
        box.Value.ShouldBe(0.2m, "and the box says so");
    }
}
