using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Filter, Fold and Drive modules, loaded off disk and driven sample by
/// sample with real state behind them.
/// </summary>
/// <remarks>
/// Signals go in through the Coordinates module's x, the way <see cref="SpaceTests"/>
/// does it and for the same reason: <see cref="CompiledPatch.Evaluate"/> takes x
/// per evaluation, which makes it the one way to feed a module an arbitrary
/// waveform without building an oscillator to produce it. The clock is passed as
/// t, which matters more here than it does there — the filter reads its own
/// sample rate off how far t moves.
/// </remarks>
public class TimbreTests
{
    private const string FilterType = "flyback.voice.filter";
    private const string FoldType = "flyback.voice.fold";
    private const string DriveType = "flyback.voice.drive";

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_plugin_offers_all_three_modules_from_one_assembly()
    {
        Catalog.Get(FilterType).ShouldNotBeNull().Name.ShouldBe("Filter");
        Catalog.Get(FoldType).ShouldNotBeNull().Name.ShouldBe("Fold");
        Catalog.Get(DriveType).ShouldNotBeNull().Name.ShouldBe("Drive");

        Catalog.ProviderOf(FilterType).ShouldBe(Catalog.ProviderOf(FoldType));
        Catalog.ProviderOf(FilterType).ShouldBe(Catalog.ProviderOf(DriveType));
    }

    // --- the filter ------------------------------------------------------------

    /// <summary>
    /// Four cells: the clock, the flag that says whether there is any memory at
    /// all, and the two integrators. Every one of them comes from the emitter's
    /// own pool, which is what lets a plugin hold state without a new opcode.
    /// </summary>
    [Fact]
    public void A_filter_takes_its_memory_from_the_emitters_own_cells()
    {
        var program = Audio(FilterType, 0);

        program.UnitCount.ShouldBe(4);
        program.DelayLengths.ShouldBeEmpty();
    }

    [Fact]
    public void A_lowpass_keeps_what_is_below_the_cutoff_and_loses_what_is_above()
    {
        var slow = Through(FilterType, Tone(60f, 4_000), 0, (1, 1_000f));
        var fast = Through(FilterType, Tone(9_000f, 4_000), 0, (1, 1_000f));

        // Settled, so what is measured is the filter rather than the first few
        // samples of one opening up.
        Energy(slow, 2_000, 4_000).ShouldBeGreaterThan(700f);
        Energy(fast, 2_000, 4_000).ShouldBeLessThan(20f);
    }

    [Fact]
    public void A_highpass_is_the_other_way_round()
    {
        var slow = Through(FilterType, Tone(60f, 4_000), 2, (1, 1_000f));
        var fast = Through(FilterType, Tone(9_000f, 4_000), 2, (1, 1_000f));

        Energy(fast, 2_000, 4_000).ShouldBeGreaterThan(Energy(slow, 2_000, 4_000) * 100f);
    }

    [Fact]
    public void Opening_the_cutoff_lets_more_through()
    {
        var tone = Tone(2_000f, 4_000);

        var shut = Energy(Through(FilterType, tone, 0, (1, 200f)), 2_000, 4_000);
        var part = Energy(Through(FilterType, tone, 0, (1, 2_000f)), 2_000, 4_000);
        var open = Energy(Through(FilterType, tone, 0, (1, 20_000f)), 2_000, 4_000);

        shut.ShouldBeLessThan(part);
        part.ShouldBeLessThan(open);
    }

    /// <summary>
    /// The cutoff is in hertz and means it. The tangent prewarps the
    /// coefficient, so the corner lands on the frequency asked for rather than
    /// near it, and the response there is one over the damping — a half, at no
    /// resonance. A decade below is unity and a decade above is the two-pole
    /// slope, a hundredth.
    /// <para>
    /// That last one is a little under a hundredth here, and the tolerance says
    /// so rather than pretending otherwise. Prewarping puts the corner exactly
    /// where it was asked for; it does not straighten the rest of the curve, and
    /// what a sampled filter does near a fifth of its own rate is bend away from
    /// the analogue prototype it was derived from.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(100f, 1f, 0.03f)]
    [InlineData(1_000f, 0.5f, 0.02f)]
    [InlineData(10_000f, 0.0073f, 0.002f)]
    public void The_corner_sits_on_the_frequency_the_cutoff_asks_for(
        float hz, float gain, float tolerance)
    {
        var heard = Through(FilterType, Tone(hz, 8_000), 0, (1, 1_000f), (2, 0f));

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
        var low = Through(FilterType, signal, 0, (1, 900f), (2, 0f));
        var band = Through(FilterType, signal, 1, (1, 900f), (2, 0f));
        var high = Through(FilterType, signal, 2, (1, 900f), (2, 0f));

        for (var i = 0; i < signal.Length; i++)
            (low[i] + 2f * band[i] + high[i]).ShouldBe(signal[i], 1e-5f);
    }

    [Fact]
    public void Resonance_peaks_the_corner()
    {
        var tone = Tone(1_000f, 8_000);

        var flat = Energy(Through(FilterType, tone, 0, (1, 1_000f), (2, 0f)), 4_000, 8_000);
        var peaked = Energy(Through(FilterType, tone, 0, (1, 1_000f), (2, 1f)), 4_000, 8_000);

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
            var output = Through(FilterType, Noise(2_000), 0, (1, cutoff), (2, resonance));

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
        var first = Add(patch, FilterType, (1, 1_000f), (2, 0f));
        var second = Add(patch, FilterType, (1, 1_000f), (2, 0f));
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(coord.Id, 0, first.Id, 0);
        patch.Connect(first.Id, 0, second.Id, 0);
        patch.Connect(second.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Catalog).Program;
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

        var once = Through(FilterType, tone, 0, (1, 1_000f), (2, 0f));

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
        foreach (var (x, seen) in Painted(FilterType, 0, (1, 900f))) seen.ShouldBe(x, 1e-6f);
        foreach (var (_, seen) in Painted(FilterType, 1, (1, 900f))) seen.ShouldBe(0f, 1e-6f);
        foreach (var (_, seen) in Painted(FilterType, 2, (1, 900f))) seen.ShouldBe(0f, 1e-6f);
    }

    // --- the folder ------------------------------------------------------------

    [Fact]
    public void A_fold_at_a_drive_of_one_is_exactly_a_wire_within_full_scale()
    {
        var signal = Noise(64);
        var output = Through(FoldType, signal, 0, (1, 1f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-6f);
    }

    /// <summary>
    /// The turn at full scale, which is the whole module: past 1 the signal comes
    /// back down rather than going on, and it keeps turning for as far as it is
    /// driven.
    /// </summary>
    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 0.5f)]
    [InlineData(2f, 0f)]
    [InlineData(3f, -1f)]
    [InlineData(5f, 1f)]
    [InlineData(-1.5f, -0.5f)]
    public void A_fold_turns_back_at_full_scale(float input, float expected)
    {
        Through(FoldType, [input], 0, (1, 1f))[0].ShouldBe(expected, 1e-5f);
    }

    [Fact]
    public void A_fold_never_leaves_full_scale_however_hard_it_is_driven()
    {
        foreach (var drive in new[] { 0f, 1f, 8f, 400f })
        foreach (var sample in Through(FoldType, Ramp(-30f, 30f, 4_000), 0, (1, drive), (2, 0.3f)))
        {
            float.IsFinite(sample).ShouldBeTrue();
            MathF.Abs(sample).ShouldBeLessThanOrEqualTo(1.000001f);
        }
    }

    [Fact]
    public void Driving_a_fold_harder_puts_more_harmonics_in()
    {
        var tone = Tone(400f, 4_000);

        // A sine has nothing above its own partial, so a highpass well clear of
        // it hears almost nothing — until the fold puts something there.
        var plain = Above(tone, 1f);
        var driven = Above(tone, 4f);

        driven.ShouldBeGreaterThan(plain * 20f);
    }

    /// <summary>
    /// Its ports are untyped, like the maths modules', so one Fold bands a color
    /// as readily as it brightens a tone — three channels folded independently,
    /// and nothing in the module aware that there were three.
    /// </summary>
    [Fact]
    public void A_fold_works_on_a_color_channel_by_channel()
    {
        var patch = new Patch();

        var color = Add(patch, "color.rgb", (0, 0.25f), (1, 0.9f), (2, 0.6f));
        var fold = Add(patch, FoldType, (1, 3f));
        var screen = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(color.Id, 0, fold.Id, 0);
        patch.Connect(fold.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0f, 0f, 0f, registers, default);

        var channels = new[] { 0.25f, 0.9f, 0.6f };
        for (var i = 0; i < channels.Length; i++)
            ((float)registers[program.OutputBase + i]).ShouldBe(Folded(channels[i] * 3f), 1e-5f);
    }

    /// <summary>
    /// It is pure, so unlike the filter beside it the fold does not care which
    /// sink is asking: the same input gives the same number with state behind it
    /// and without.
    /// </summary>
    [Fact]
    public void A_fold_is_the_same_module_at_both_sinks()
    {
        foreach (var (x, seen) in Painted(FoldType, 0, (1, 2.5f)))
            seen.ShouldBe(Through(FoldType, [x], 0, (1, 2.5f))[0], 1e-6f);
    }

    // --- the saturator ---------------------------------------------------------

    /// <summary>
    /// What the normalisation is for: the curve is divided by what it does to a
    /// full-scale input, so drive changes the shape of a signal and never the
    /// height of it.
    /// </summary>
    [Fact]
    public void Driving_harder_never_makes_it_louder()
    {
        foreach (var drive in new[] { 0f, 1f, 2f, 8f, 16f })
        {
            Through(DriveType, [1f], 0, (1, drive))[0].ShouldBe(1f, 1e-5f);
            Through(DriveType, [-1f], 0, (1, drive))[0].ShouldBe(-1f, 1e-5f);

            foreach (var sample in Through(DriveType, Ramp(-1f, 1f, 500), 0, (1, drive)))
                MathF.Abs(sample).ShouldBeLessThanOrEqualTo(1.000001f);
        }
    }

    /// <summary>
    /// And what it does instead: the quiet parts come up while the loud ones stop
    /// moving, which is the same arithmetic a compressor is.
    /// </summary>
    [Fact]
    public void Driving_harder_brings_the_quiet_parts_up()
    {
        var gentle = Through(DriveType, [0.1f], 0, (1, 1f))[0];
        var hard = Through(DriveType, [0.1f], 0, (1, 12f))[0];

        hard.ShouldBeGreaterThan(gentle);
        gentle.ShouldBeGreaterThan(0.1f);
    }

    [Fact]
    public void At_no_drive_it_is_very_nearly_a_wire()
    {
        foreach (var x in new[] { -1f, -0.4f, 0f, 0.25f, 1f })
            Through(DriveType, [x], 0, (1, 0f))[0].ShouldBe(x, 0.02f);
    }

    [Fact]
    public void A_saturator_is_the_same_module_at_both_sinks()
    {
        foreach (var (x, seen) in Painted(DriveType, 0, (1, 6f)))
            seen.ShouldBe(Through(DriveType, [x], 0, (1, 6f))[0], 1e-6f);
    }

    // --- the preset ------------------------------------------------------------

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Filter sweep").Build(loaded.Modules);

        var types = patch.Nodes.Select(n => n.TypeId).ToList();
        types.ShouldContain(FilterType);
        types.ShouldContain(FoldType);

        var video = patch.CompileForVideo(loaded.Modules);
        video.Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // The filter is in the sound and not in the picture, so only one of the
        // two programs carries cells for it.
        audio.Program.UnitCount.ShouldBe(4);
        video.Program.UnitCount.ShouldBe(0);
    }

    // --- harness ----------------------------------------------------------------

    /// <summary>
    /// Feeds a signal through one module into the audio sink, one sample at a
    /// time, with whatever state the program asked for.
    /// </summary>
    /// <param name="signal"></param>
    /// <param name="port">Which of the module's outputs to listen to.</param>
    /// <param name="typeId"></param>
    /// <param name="knobs"></param>
    private static float[] Through(
        string typeId, float[] signal, int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Catalog).Program;
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
    private static (float X, float Seen)[] Painted(
        string typeId, int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
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

    /// <summary>The audio program for one module, for the tests that read what it asked the renderer for.</summary>
    private static CompiledPatch Audio(string typeId, int port)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId);
        var sink = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, sink.Id, NodeCatalog.OutputLeftPort);

        return patch.CompileForAudio(Catalog).Program;
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// How much of a folded tone sits well above its own fundamental, measured
    /// through the filter's highpass — which is one module of this plugin used to
    /// take the measurement of another.
    /// </summary>
    private static float Above(float[] tone, float drive)
    {
        var folded = Through(FoldType, tone, 0, (1, drive));
        var partials = Through(FilterType, folded, 2, (1, 1_500f), (2, 0f));

        return Energy(partials, 1_000, tone.Length);
    }

    private static float[] Tone(float hz, int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(MathF.Tau * hz * i / Rate))];

    private static float[] Ramp(float from, float to, int length) =>
        [.. Enumerable.Range(0, length).Select(i => from + (to - from) * i / (length - 1f))];

    /// <summary>Deterministic, so a failure is reproducible.</summary>
    private static float[] Noise(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(i * 12.9898f) * 0.5f)];

    /// <summary>The fold, written out the way the module's own comment describes it.</summary>
    private static float Folded(float x)
    {
        var phase = 0.25f * x + 0.75f;
        return 4f * MathF.Abs(phase - MathF.Floor(phase) - 0.5f) - 1f;
    }

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
