using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Filter module, driven sample by sample with real state behind it.
/// </summary>
/// <remarks>
/// Signals go in through Coordinates' x, and the clock is passed as t: the filter
/// reads its own sample rate off how far t moves.
/// </remarks>
public class FilterTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_shaping_for_audio()
    {
        var def = Modules.Get(NodeCatalog.FilterTypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Filter");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Outputs.Select(p => p.Name).ShouldBe(["low", "band", "high"]);
    }

    /// <summary>
    /// Four cells: the clock, the flag that says whether there is any memory at
    /// all, and the two integrators. Every one of them comes from the emitter's
    /// own pool, which is what lets a module hold state without a new opcode.
    /// </summary>
    [Fact]
    public void A_filter_takes_its_memory_from_the_emitters_own_cells()
    {
        var program = Audio(0);

        program.UnitCount.ShouldBe(4);
        program.DelayLengths.ShouldBeEmpty();
    }

    [Fact]
    public void A_lowpass_keeps_what_is_below_the_cutoff_and_loses_what_is_above()
    {
        var slow = Through(Tone(60f, 4_000), 0, (1, 1_000f));
        var fast = Through(Tone(9_000f, 4_000), 0, (1, 1_000f));

        // Settled, so what is measured is the filter rather than the first few
        // samples of one opening up.
        Energy(slow, 2_000, 4_000).ShouldBeGreaterThan(700f);
        Energy(fast, 2_000, 4_000).ShouldBeLessThan(20f);
    }

    [Fact]
    public void A_highpass_is_the_other_way_round()
    {
        var slow = Through(Tone(60f, 4_000), 2, (1, 1_000f));
        var fast = Through(Tone(9_000f, 4_000), 2, (1, 1_000f));

        Energy(fast, 2_000, 4_000).ShouldBeGreaterThan(Energy(slow, 2_000, 4_000) * 100f);
    }

    [Fact]
    public void Opening_the_cutoff_lets_more_through()
    {
        var tone = Tone(2_000f, 4_000);

        var shut = Energy(Through(tone, 0, (1, 200f)), 2_000, 4_000);
        var part = Energy(Through(tone, 0, (1, 2_000f)), 2_000, 4_000);
        var open = Energy(Through(tone, 0, (1, 20_000f)), 2_000, 4_000);

        shut.ShouldBeLessThan(part);
        part.ShouldBeLessThan(open);
    }

    /// <summary>
    /// The cutoff is in hertz and means it. The tangent prewarps the coefficient, so
    /// the corner lands on the frequency asked for and the response there is one over
    /// the damping — a half, at no resonance.
    /// <para>
    /// A decade above is a little under the two-pole hundredth, and the tolerance says
    /// so: prewarping puts the corner where it was asked for and does not straighten
    /// the rest of the curve.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(100f, 1f, 0.03f)]
    [InlineData(1_000f, 0.5f, 0.02f)]
    [InlineData(10_000f, 0.0073f, 0.002f)]
    public void The_corner_sits_on_the_frequency_the_cutoff_asks_for(
        float hz, float gain, float tolerance)
    {
        var heard = Through(Tone(hz, 8_000), 0, (1, 1_000f), (2, 0f));

        Peak(heard, 4_000, 8_000).ShouldBe(gain, tolerance);
    }

    /// <summary>
    /// The identity the topology is built on: what went in is the three responses
    /// added back together, with the band weighted by the damping. It holds
    /// exactly, sample by sample, and it is what makes the outputs three views of
    /// one filter rather than three filters.
    /// </summary>
    [Fact]
    public void The_three_outputs_add_back_up_to_what_went_in()
    {
        var signal = Noise(512);

        // Damping is 2 at no resonance, which is the weight the band carries.
        var low = Through(signal, 0, (1, 900f), (2, 0f));
        var band = Through(signal, 1, (1, 900f), (2, 0f));
        var high = Through(signal, 2, (1, 900f), (2, 0f));

        for (var i = 0; i < signal.Length; i++)
            (low[i] + 2f * band[i] + high[i]).ShouldBe(signal[i], 1e-5f);
    }

    [Fact]
    public void Resonance_peaks_the_corner()
    {
        var tone = Tone(1_000f, 8_000);

        var flat = Energy(Through(tone, 0, (1, 1_000f), (2, 0f)), 4_000, 8_000);
        var peaked = Energy(Through(tone, 0, (1, 1_000f), (2, 1f)), 4_000, 8_000);

        peaked.ShouldBeGreaterThan(flat * 4f);
    }

    /// <summary>
    /// Solving the loop rather than iterating it is what buys stability at any
    /// cutoff, and a sweep is what tries it: the cutoff passes through everything
    /// on the way to wherever it is going, including the far end where the
    /// coefficient is clamped.
    /// </summary>
    [Fact]
    public void A_filter_stays_bounded_wherever_the_cutoff_is_swept()
    {
        foreach (var cutoff in new[] { 0f, 20f, 5_000f, 100_000f, 1e9f, -400f })
        foreach (var resonance in new[] { 0f, 1f, 4f, -1f })
        {
            var output = Through(Noise(2_000), 0, (1, cutoff), (2, resonance));

            foreach (var sample in output)
            {
                float.IsFinite(sample).ShouldBeTrue();
                MathF.Abs(sample).ShouldBeLessThan(8f);
            }
        }
    }

    /// <summary>
    /// Six cells rather than eight: each filter keeps its own two integrators,
    /// and both read the one clock and the one flag the emitter holds for the
    /// whole program (ADR-0042). What is separate is what has to be — two filters
    /// in series are steeper than one, which is the audible form of the same
    /// fact — and what is shared is what could not differ.
    /// </summary>
    [Fact]
    public void Two_filters_in_one_patch_keep_their_cells_apart()
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var first = Add(patch, NodeCatalog.FilterTypeId, (1, 1_000f), (2, 0f));
        var second = Add(patch, NodeCatalog.FilterTypeId, (1, 1_000f), (2, 0f));
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(coord.Id, 0, first.Id, 0);
        patch.Connect(first.Id, 0, second.Id, 0);
        patch.Connect(second.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Modules).Program;
        program.UnitCount.ShouldBe(6);

        var delays = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var tone = Tone(4_000f, 4_000);
        var twice = new float[tone.Length];

        for (var i = 0; i < tone.Length; i++)
        {
            program.Evaluate(tone[i], 0f, i / (double)Rate, registers, default, delays);
            twice[i] = (float)registers[program.OutputBase];
        }

        var once = Through(tone, 0, (1, 1_000f), (2, 0f));

        Peak(twice, 2_000, 4_000).ShouldBeLessThan(Peak(once, 2_000, 4_000) * 0.5f);
    }

    /// <summary>
    /// A picture is one evaluation per pixel with nothing before it, so what the
    /// filter sees there is a signal that never moves — and its response to a
    /// signal that never moves is exactly this. The patch draws what it drew
    /// before the filter was put in it.
    /// </summary>
    [Fact]
    public void With_no_state_a_lowpass_is_a_wire_and_the_other_two_are_silent()
    {
        foreach (var (x, seen) in Painted(0, (1, 900f))) seen.ShouldBe(x, 1e-6f);
        foreach (var (_, seen) in Painted(1, (1, 900f))) seen.ShouldBe(0f, 1e-6f);
        foreach (var (_, seen) in Painted(2, (1, 900f))) seen.ShouldBe(0f, 1e-6f);
    }

    // --- harness ----------------------------------------------------------------

    /// <summary>
    /// Feeds a signal through one Filter into the audio sink, one sample at a
    /// time, with whatever state the program asked for.
    /// </summary>
    private static float[] Through(float[] signal, int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.FilterTypeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Modules).Program;
        var delays = new DelayState(
            program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);

        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)Rate, registers, default, delays);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>Compiles the module into the video sink and reads it with no state, as SynthRenderer does.</summary>
    private static (float X, float Seen)[] Painted(int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.FilterTypeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Modules).Program;
        var registers = program.AllocateRegisters();

        return
        [
            .. new[] { -0.75f, 0f, 0.25f, 0.5f, 1f }.Select(x =>
            {
                program.Evaluate(x, 0f, 0f, registers, default);
                return (x, (float)registers[program.OutputBase]);
            }),
        ];
    }

    /// <summary>The audio program for one Filter, for the tests that read what it asked the renderer for.</summary>
    private static CompiledPatch Audio(int port)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.FilterTypeId);
        var sink = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, sink.Id, NodeCatalog.OutputLeftPort);

        return patch.CompileForAudio(Modules).Program;
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Modules.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static float[] Tone(float hz, int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(MathF.Tau * hz * i / Rate))];

    /// <summary>Deterministic, so a failure is reproducible.</summary>
    private static float[] Noise(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(i * 12.9898f) * 0.5f)];

    private static float Peak(float[] signal, int from, int to)
    {
        var most = 0f;
        for (var i = from; i < to && i < signal.Length; i++) most = MathF.Max(most, MathF.Abs(signal[i]));
        return most;
    }

    private static float Energy(float[] signal, int from, int to)
    {
        var sum = 0f;
        for (var i = from; i < to && i < signal.Length; i++) sum += signal[i] * signal[i];
        return sum;
    }
}
