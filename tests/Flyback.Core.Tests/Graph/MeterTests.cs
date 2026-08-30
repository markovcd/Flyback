using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Meter: how loud the speakers are, as a number the picture can use.
/// </summary>
/// <remarks>
/// It taps its input the way a Scope does, and everything after that is the
/// opposite of one. A Scope carries a stretch of the past across as a buffer and
/// reads it with a table; this carries the same stretch across as two numbers and
/// is <em>told</em> them, through the same live inputs a keyboard is played on. So
/// most of what is worth pinning here is what the picture's program does
/// <em>not</em> contain: no table, no chart buffer, and none of the signal chain
/// the meter is listening to.
/// </remarks>
public class MeterTests
{
    private const string Meter = NodeCatalog.MeterTypeId;
    private const string Scope = NodeCatalog.ScopeTypeId;
    private const string Sine = "osc.sine";

    private const int In = 0;
    private const int Window = 1;
    private const int Scale = 2;

    private const int Level = 0;
    private const int Peak = 1;

    // --- what it is ------------------------------------------------------------

    [Fact]
    public void A_meter_taps_what_it_listens_to_and_charts_none_of_it()
    {
        var def = NodeCatalog.BuiltIn.Require(Meter);

        def.TapsSignal.ShouldBeTrue();
        def.ChartsSignal.ShouldBeFalse();

        def.Outputs.Select(o => o.Name).ShouldBe(["level", "peak"]);
        def.Inputs[In].Swept.ShouldBeTrue();
    }

    /// <summary>
    /// The claim the whole module rests on. A Scope's chart is a table read, and
    /// a table is the one thing the shader cannot draw — so a patch charting
    /// sound draws on the processor for as long as it is loaded. What arrives
    /// here is a number instead, which lowers to a uniform.
    /// </summary>
    [Fact]
    public void A_metered_picture_keeps_the_shader_where_a_charted_one_loses_it()
    {
        var metered = Drawn(Meter);
        var charted = Drawn(Scope);

        metered.Tables.Count.ShouldBe(0);
        charted.Tables.Count.ShouldBeGreaterThan(0);

        metered.LiveInputs.Count.ShouldBe(2);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(metered, dialect).PatchFragment.ShouldContain("uLive[");
    }

    /// <summary>
    /// And it costs the picture nothing else either: the input is swept, so the
    /// signal being listened to is never lowered into the frame. A patch whose
    /// hue follows a bass line does not compute a bass line per pixel.
    /// </summary>
    [Fact]
    public void The_picture_never_lowers_the_signal_a_meter_is_listening_to()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var meter = b.Add(Meter, 400, 0);

        // Something the picture would notice the cost of, if it were lowered.
        var chain = b.Add(Sine, 0, 0);
        for (var i = 0; i < 8; i++)
        {
            var next = b.Add(Sine, 0, i * 100);
            b.Wire(chain, 0, next, 0);
            chain = next;
        }

        b.Wire(chain, 0, meter, In).Wire(meter, Level, output, NodeCatalog.OutputColorPort);

        var drawn = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        drawn.Ops.Count(op => op.Code == OpCode.Sin).ShouldBe(0);
        drawn.Ops.Count(op => op.Code == OpCode.LoadLive).ShouldBe(2);

        // The speakers, by contrast, evaluate the whole chain — the socket is a
        // root of that program whether or not anything downstream reads it.
        var heard = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        heard.Ops.Count(op => op.Code == OpCode.Sin).ShouldBe(9);
        heard.Taps.Count.ShouldBe(1);
    }

    /// <summary>
    /// A meter is measured and never charted, so unlike a Scope it asks the
    /// screen's program for no buffer and asks whatever refills those for no
    /// work. That is what the second flag buys and the only thing it buys.
    /// </summary>
    [Fact]
    public void A_meter_asks_the_picture_for_no_chart_buffer()
    {
        Drawn(Meter).Taps.Count.ShouldBe(0);
        Drawn(Scope).Taps.Count.ShouldBe(1);

        // Both still tap, which is the half of it that did not change.
        Heard(Meter).Taps.Count.ShouldBe(1);
        Heard(Scope).Taps.Count.ShouldBe(1);
    }

    /// <summary>
    /// Two meters are two readings. They are named by node id because the two
    /// programs of a patch are compiled separately and share no numbering — the
    /// same problem, and the same answer, as a Scope's buffer.
    /// </summary>
    [Fact]
    public void Every_meter_listens_on_a_name_of_its_own()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var first = b.Add(Meter, 400, 0);
        var second = b.Add(Meter, 400, 200);
        var both = b.Add("math.add", 650, 100);

        b.Wire(first, Level, both, 0)
         .Wire(second, Peak, both, 1)
         .Wire(both, 0, output, NodeCatalog.OutputColorPort);

        var drawn = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        drawn.LiveInputs.ShouldContain(Meters.Key(first.Id, Meters.Level));
        drawn.LiveInputs.ShouldContain(Meters.Key(second.Id, Meters.Peak));
        drawn.LiveInputs.Distinct().Count().ShouldBe(drawn.LiveInputs.Count);
    }

    // --- the measurement -------------------------------------------------------

    /// <summary>
    /// A tone of a known height, measured. The peak is the height and the
    /// loudness is that over root two, which is what a level meter shows and
    /// what a musician expects — and the pair of them being different is why
    /// both come out at once.
    /// </summary>
    [Fact]
    public void A_tone_measures_its_own_height_and_its_own_loudness()
    {
        const int rate = GlobalConstants.SampleRate;
        const float height = 0.8f;

        var memory = new DelayState([], rate, traceCount: 1);

        for (var i = 0; i < rate; i++)
            memory.Tap(0, height * Math.Sin(Math.Tau * 100d * i / rate));

        var (peak, level) = memory.Measure(0, rate / 10);

        peak.ShouldBe(height, 0.001f);
        level.ShouldBe(height / MathF.Sqrt(2f), 0.01f);
    }

    [Fact]
    public void Silence_measures_as_nothing_and_an_empty_ring_as_nothing()
    {
        var memory = new DelayState([], GlobalConstants.SampleRate, traceCount: 1);

        memory.Measure(0, 4_800).ShouldBe((0f, 0f));

        // A slot the program does not have, which is what a block left over from
        // the program before a recompile looks like.
        memory.Measure(7, 4_800).ShouldBe((0f, 0f));
    }

    /// <summary>
    /// The window is what makes the level steady or twitchy, and it is the only
    /// smoothing there is: a burst fills a short window and is diluted by a long
    /// one.
    /// </summary>
    [Fact]
    public void A_longer_window_dilutes_a_burst_that_a_short_one_is_full_of()
    {
        const int rate = GlobalConstants.SampleRate;

        var memory = new DelayState([], rate, traceCount: 1);

        for (var i = 0; i < rate; i++) memory.Tap(0, i >= rate - 480 ? 1d : 0d);

        var brief = memory.Measure(0, 480);
        var patient = memory.Measure(0, 4_800);

        brief.Level.ShouldBe(1f, 1e-4f);
        patient.Level.ShouldBe(MathF.Sqrt(0.1f), 0.01f);

        // The peak is the peak whichever window it is looked for in, which is
        // the difference between the two readings.
        brief.Peak.ShouldBe(1f);
        patient.Peak.ShouldBe(1f);
    }

    // --- the crossing ----------------------------------------------------------

    /// <summary>
    /// End to end, and the whole point: the speakers play, something outside
    /// both programs listens, and the picture is a different color for it —
    /// without one op of the sound being evaluated by the frame.
    /// </summary>
    [Fact]
    public void What_the_speakers_played_reaches_the_picture()
    {
        var patch = Listening(Sine, (Window, -1.3f));

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var block = new LiveValues(drawn.LiveInputs);

        // Nothing played yet, so nothing heard: the promise is the Scope's.
        Meters.Refresh(heard, null, block);
        Lit(drawn, block).ShouldBe(0d);

        var memory = Played(heard);
        Meters.Refresh(heard, memory, block);

        // A full-scale sine through the Output's own gain, which the preset
        // default leaves at half.
        Lit(drawn, block).ShouldBeGreaterThan(0.2d);
        Lit(drawn, block).ShouldBeLessThan(1d);

        // And it goes back to nothing when the sound stops, unlike a chart,
        // which holds its last sweep.
        Meters.Silence(heard, block);
        Lit(drawn, block).ShouldBe(0d);
    }

    /// <summary>
    /// 'scale' is an ordinary socket rather than a number read at compile time,
    /// because it is applied after the reading arrives — so it may be swept, and
    /// it is how a quiet signal is brought up to something a hue can use.
    /// </summary>
    [Fact]
    public void Scale_brings_a_quiet_signal_up()
    {
        var patch = Listening(Sine, (Window, -1.3f), (Scale, 0.25f));

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var block = new LiveValues(drawn.LiveInputs);
        Meters.Refresh(heard, Played(heard), block);

        Lit(drawn, block).ShouldBeGreaterThan(1d);
    }

    /// <summary>
    /// A Scope taps the same way and wants none of this. Its ring is offered and
    /// refused by nobody reading the name, which is what keeps the measurement
    /// off every patch that only charts.
    /// </summary>
    [Fact]
    public void A_scope_is_offered_a_reading_and_nobody_takes_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 0);

        var source = b.Add(Sine, 0, 0);
        var scope = b.Add(Scope, 400, 0);
        b.Wire(source, 0, scope, In);

        var heard = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var block = new LiveValues([Meters.Key(scope.Id, Meters.Level)]);

        // Written when something does read it — the block here is contrived to,
        // which is how this test knows the ring was measured at all.
        Meters.Refresh(heard, Played(heard), block);
        block.At(0).ShouldBeGreaterThan(0d);

        // And not written into a block that does not, which is every real
        // patch's: a Scope's picture never names one of these.
        var indifferent = new LiveValues(["keyboard/gate"]);
        Meters.Refresh(heard, Played(heard), indifferent);
        indifferent.At(0).ShouldBe(0d);
    }

    // --- harness ---------------------------------------------------------------

    /// <summary>
    /// A patch whose Output is lit by a meter listening to <paramref name="watched"/>,
    /// and whose sound is that same signal.
    /// </summary>
    private static Patch Listening(
        string watched, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0, (NodeCatalog.OutputGainPort, 1f));
        var source = b.Add(watched, 0, 0, (1, 220f));
        var meter = b.Add(Meter, 400, 0, knobs);

        b.Wire(source, 0, meter, In)
         .Wire(source, 0, output, NodeCatalog.OutputLeftPort)
         .Wire(meter, Level, output, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    /// <summary>What the picture makes of it, read off the red channel.</summary>
    private static double Lit(CompiledPatch drawn, LiveValues block)
    {
        var registers = drawn.AllocateRegisters();
        drawn.Evaluate(0d, 0d, 0d, registers, default, live: block);

        return registers[drawn.OutputBase];
    }

    /// <summary>Runs the sound long enough to fill any window a knob can ask for.</summary>
    private static DelayState? Played(CompiledPatch heard, int frames = 8_192)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);

        renderer.Render(heard, new float[frames * 2], AudioScan.TimeDriven, memory);

        return memory;
    }

    /// <summary>The screen's program for a patch holding one of these off to the side.</summary>
    private static CompiledPatch Drawn(string typeId) => Compiled(typeId, plays: false);

    private static CompiledPatch Heard(string typeId) => Compiled(typeId, plays: true);

    private static CompiledPatch Compiled(string typeId, bool plays)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        var source = b.Add(Sine, 0, 0);
        var watcher = b.Add(typeId, 400, 0);

        b.Wire(source, 0, watcher, In)
         .Wire(watcher, 0, output, NodeCatalog.OutputColorPort);

        return plays
            ? b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program
            : b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
    }
}
