using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Bridge: a value the sound works out, read by the picture once a frame.
/// </summary>
/// <remarks>
/// A Meter's plumbing with the value's own mean as the reading, so a sign survives,
/// a wrapping phase crosses its top rather than smearing through the middle, and a
/// trigger one evaluation long is still seen.
/// </remarks>
public class BridgeTests
{
    private const string Bridge = BridgeModule.TypeId;
    private const string Value = SourceModules.ValueTypeId;
    private const string Sine = "osc.sine";

    private const int In = 0;
    private const int Window = 1;
    private const int Wrap = 2;

    private const int Mean = 0;
    private const int Top = 1;

    private const int Rate = GlobalConstants.SampleRate;

    // --- what it is ------------------------------------------------------------

    [Fact]
    public void A_bridge_taps_what_it_carries_and_charts_none_of_it()
    {
        var def = NodeCatalog.BuiltIn.Require(Bridge);

        def.TapsSignal.ShouldBeTrue();
        def.ChartsSignal.ShouldBeFalse();
        def.Inputs[In].Swept.ShouldBeTrue();
        def.Inputs.Select(i => i.Name).ShouldBe(["in", "window", "wrap"]);
        def.Outputs.Select(o => o.Name).ShouldBe(["value", "top"]);
    }

    /// <summary>
    /// The signal behind it is never lowered into the frame, and what arrives is a
    /// uniform: a picture moved by an envelope reads one number rather than
    /// working the envelope out per pixel.
    /// </summary>
    [Fact]
    public void The_picture_reads_a_number_and_never_lowers_the_signal_behind_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(OutputModule.TypeId, 900, 0);
        var bridge = b.Add(Bridge, 400, 0);

        var chain = b.Add(Sine, 0, 0);
        for (var i = 0; i < 8; i++)
        {
            var next = b.Add(Sine, 0, i * 100);
            b.Wire(chain, 0, next, 0);
            chain = next;
        }

        b.Wire(chain, 0, bridge, In).Wire(bridge, Mean, output, OutputModule.ColorPort);

        var drawn = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        drawn.Ops.Count(op => op.Code == OpCode.Sin).ShouldBe(0);
        drawn.Tables.Count.ShouldBe(0);
        drawn.Ops.Count(op => op.Code == OpCode.LoadLive).ShouldBe(1);
        drawn.LiveInputs.ShouldContain(MeterSignals.Key(bridge.Id, MeterSignals.Value));

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(drawn, dialect).PatchFragment.ShouldContain("uLive[");

        b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Taps.Count.ShouldBe(1);
    }

    [Fact]
    public void The_wrap_knob_is_read_into_the_tap_and_other_taps_wrap_at_nothing()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(OutputModule.TypeId, 900, 0);
        var source = b.Add(Sine, 0, 0);
        var bridge = b.Add(Bridge, 400, 0, (Wrap, 256f));
        var meter = b.Add(MeterModule.TypeId, 400, 200);

        b.Wire(source, 0, bridge, In).Wire(source, 0, meter, In);

        var taps = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.Taps;

        taps.Single(t => t.Node == bridge.Id).Wrap.ShouldBe(256f);
        taps.Single(t => t.Node == meter.Id).Wrap.ShouldBe(0f);
    }

    // --- the reading -----------------------------------------------------------

    /// <summary>
    /// The difference from a Meter: a Meter reads loudness, so a value below
    /// nought reads as its size, and a patch had to lift everything above nought
    /// before metering it. The mean keeps the sign.
    /// </summary>
    [Fact]
    public void A_value_below_nought_keeps_its_sign()
    {
        var memory = Filled(_ => -0.3d);

        memory.Average(0, 480).Mean.ShouldBe(-0.3f, 1e-5f);
        memory.Measure(0, 480).Level.ShouldBe(0.3f, 1e-5f);
    }

    [Fact]
    public void A_ramp_reads_as_its_middle_over_the_window()
    {
        var memory = Filled(i => i);

        memory.Average(0, 101).Mean.ShouldBe(Rate - 51f, 0.5f);
    }

    /// <summary>
    /// The point of 'wrap'. A phase that crossed its top inside the window is
    /// just past the top, and a plain mean says it is halfway round, which is
    /// what made a picture driven by a phase jump once a turn.
    /// </summary>
    [Fact]
    public void A_phase_crossing_its_top_reads_beside_the_top_and_not_in_the_middle()
    {
        // Half the window at 0.98, half at 0.02.
        var memory = Filled(i => i < Rate - 50 ? 0.98d : 0.02d);

        memory.Average(0, 100).Mean.ShouldBe(0.5f, 1e-4f);

        var circular = memory.Average(0, 100, wrap: 1f).Mean;
        Math.Min(circular, 1f - circular).ShouldBeLessThan(1e-3f);
    }

    [Fact]
    public void A_wrapped_value_reads_between_nought_and_the_wrap()
    {
        var memory = Filled(_ => 200d);

        memory.Average(0, 100, wrap: 256f).Mean.ShouldBe(200f, 0.01f);

        // A count past its wrap reads where it is on the circle.
        Filled(_ => 300d).Average(0, 100, wrap: 256f).Mean.ShouldBe(44f, 0.01f);
        Filled(_ => -0.25d).Average(0, 100, wrap: 1f).Mean.ShouldBe(0.75f, 1e-4f);
    }

    /// <summary>
    /// A trigger one evaluation long averages to almost nothing over a frame,
    /// and a frame read of the value at one instant would miss it nearly always.
    /// The top of the window sees it.
    /// </summary>
    [Fact]
    public void A_trigger_one_evaluation_long_is_seen_at_the_top()
    {
        var memory = Filled(i => i == Rate - 300 ? 1d : 0d);

        var (mean, top) = memory.Average(0, 960);

        top.ShouldBe(1f);
        mean.ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void An_empty_ring_and_a_missing_slot_read_as_nothing()
    {
        var memory = new DelayState([], Rate, traceCount: 1);

        memory.Average(0, 480).ShouldBe((0f, 0f));
        memory.Average(3, 480, wrap: 1f).ShouldBe((0f, 0f));
    }

    // --- the crossing ----------------------------------------------------------

    /// <summary>
    /// End to end: the speakers play, the reading is played in, and the picture
    /// is lit by the value itself, sign and all, then by nothing once the sound
    /// stops.
    /// </summary>
    [Fact]
    public void What_the_sound_holds_reaches_the_picture()
    {
        var patch = Carrying(-0.4f, Mean);

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var block = new LiveValues(drawn.LiveInputs);

        Meters.Refresh(heard, null, block);
        Lit(drawn, block).ShouldBe(0d);

        Meters.Refresh(heard, Played(heard), block);
        Lit(drawn, block).ShouldBe(-0.4d, 1e-4);

        Meters.Silence(heard, block);
        Lit(drawn, block).ShouldBe(0d);
    }

    [Fact]
    public void A_wrapped_bridge_reaches_the_picture_on_its_circle()
    {
        var patch = Carrying(1.25f, Mean, (Wrap, 1f));

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var block = new LiveValues(drawn.LiveInputs);
        Meters.Refresh(heard, Played(heard), block);

        Lit(drawn, block).ShouldBe(0.25d, 1e-4);
    }

    // --- harness ---------------------------------------------------------------

    /// <summary>A second of ring, filled by <paramref name="at"/> of each evaluation's index.</summary>
    private static DelayState Filled(Func<int, double> at)
    {
        var memory = new DelayState([], Rate, traceCount: 1);

        for (var i = 0; i < Rate; i++) memory.Tap(0, at(i));

        return memory;
    }

    /// <summary>A patch whose Output is lit by a Bridge carrying a held <paramref name="held"/>.</summary>
    private static Patch Carrying(float held, int reading, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(OutputModule.TypeId, 900, 0);
        var source = b.Add(Value, 0, 0, (0, held));
        var bridge = b.Add(Bridge, 400, 0, [(Window, -2f), .. knobs]);

        b.Wire(source, 0, bridge, In).Wire(bridge, reading, output, OutputModule.ColorPort);

        return b.Patch;
    }

    private static double Lit(CompiledPatch drawn, LiveValues block)
    {
        var registers = drawn.AllocateRegisters();
        drawn.Evaluate(0d, 0d, 0d, registers, default, live: block);

        return registers[drawn.OutputBase];
    }

    private static DelayState? Played(CompiledPatch heard, int frames = 8_192)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(heard);

        renderer.Render(heard, new float[frames * 2], memory);

        return memory;
    }
}
