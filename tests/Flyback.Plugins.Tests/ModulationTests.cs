using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Chorus, Flanger and Phaser modules, loaded off disk and driven sample by
/// sample with real state behind them.
/// </summary>
/// <remarks>
/// Signals go in through the Coordinates module's x and the clock through t, the
/// way <see cref="SpaceTests"/> and <see cref="TimbreTests"/> both do it. What is
/// mostly being pinned here is movement, which means measuring the same patch at
/// two different moments rather than measuring one number.
/// </remarks>
public class ModulationTests
{
    private const string ChorusType = "flyback.effects.chorus";
    private const string FlangerType = "flyback.effects.flanger";
    private const string PhaserType = "flyback.effects.phaser";

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    /// <summary>Port indices are the same shape across the three, apart from the flanger's extra feedback.</summary>
    private const int Rate1 = 1;
    private const int Depth = 2;

    [Fact]
    public void The_plugin_offers_all_three_modules_from_one_assembly()
    {
        Catalog.Get(ChorusType).ShouldNotBeNull().Name.ShouldBe("Chorus");
        Catalog.Get(FlangerType).ShouldNotBeNull().Name.ShouldBe("Flanger");
        Catalog.Get(PhaserType).ShouldNotBeNull().Name.ShouldBe("Phaser");

        Catalog.ProviderOf(ChorusType).ShouldBe(Catalog.ProviderOf(PhaserType));
        Catalog.Get(ChorusType)!.Category.ShouldBe(ModuleCategories.TimeEffects);
    }

    /// <summary>
    /// The promise the Delay module makes, kept by all three: at no mix the
    /// module is the wire it was before anybody put it there.
    /// </summary>
    [Theory]
    [InlineData(ChorusType, 3)]
    [InlineData(FlangerType, 4)]
    [InlineData(PhaserType, 4)]
    public void At_no_mix_every_one_of_them_is_exactly_a_wire(string typeId, int mix)
    {
        var signal = Noise(256);
        var output = Through(typeId, signal, 0, (mix, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-6f);
    }

    /// <summary>
    /// What makes all three effects rather than filters: hold the input still and
    /// the output still moves, because the thing being swept is inside the module.
    /// </summary>
    [Theory]
    [InlineData(ChorusType)]
    [InlineData(FlangerType)]
    [InlineData(PhaserType)]
    public void The_sweep_is_inside_the_module(string typeId)
    {
        var output = Through(typeId, Tone(500f, Rate), 0, (Rate1, 1f), (Depth, 1f));

        // One second of a steady 500 Hz tone, one full turn of the sweep, read
        // as the level over each short window across it. A module that only
        // filtered would hold one level throughout; these drag their notches
        // through the tone instead, so the level goes somewhere and comes back.
        Spread(output, 2_000, Rate).ShouldBeGreaterThan(0.1f);
    }

    /// <summary>
    /// Every module here hands its own sweep back out, and that output is the one
    /// part of them the video path can use: a phase accumulator falls back to the
    /// multiply it replaced where there is no state (ADR-0030), so the sine is the
    /// same sine drawn as heard.
    /// </summary>
    [Theory]
    [InlineData(ChorusType, 2)]
    [InlineData(FlangerType, 1)]
    [InlineData(PhaserType, 1)]
    public void The_sweep_comes_back_out_and_is_the_same_at_both_sinks(string typeId, int port)
    {
        var heard = Through(typeId, new float[Rate], port, (Rate1, 1f));

        // One cycle a second, so a quarter of the way in is the top of the sine.
        heard[0].ShouldBe(0f, 1e-3f);
        heard[Rate / 4].ShouldBe(1f, 1e-3f);
        heard[Rate / 2].ShouldBe(0f, 1e-3f);

        // And the same drawn: the video path has no state, and this output does
        // not need any.
        var painted = PaintedAt(typeId, port, 0.25f, (Rate1, 1f));
        painted.ShouldBe(1f, 1e-3f);
    }

    // --- the chorus ------------------------------------------------------------

    /// <summary>
    /// Its two outputs are swept in opposite directions, which is the whole of
    /// what makes it wide rather than merely wobbly. They are never the same
    /// signal except at the two moments the sweep crosses the middle.
    /// </summary>
    [Fact]
    public void A_chorus_sweeps_its_two_outputs_apart()
    {
        var tone = Tone(400f, 24_000);

        var near = Through(ChorusType, tone, 0, (Rate1, 1f), (Depth, 1f));
        var far = Through(ChorusType, tone, 1, (Rate1, 1f), (Depth, 1f));

        var apart = 0f;
        for (var i = 6_000; i < 24_000; i++) apart = MathF.Max(apart, MathF.Abs(near[i] - far[i]));

        apart.ShouldBeGreaterThan(0.2f);
    }

    [Fact]
    public void A_chorus_holds_two_delay_lines_and_no_feedback()
    {
        var program = Audio(ChorusType, 0);

        program.DelayLengths.Count.ShouldBe(2);

        // Both sized for the far end of the sweep, since a buffer cannot be
        // resized from the audio thread even though the delay can be swept.
        foreach (var length in program.DelayLengths) length.ShouldBe(0.022f, 1e-6f);
    }

    /// <summary>
    /// A delay is a wire where there is no state, so a chorus made of two of them
    /// is a crossfade between the dry signal and itself — which is the dry signal.
    /// It needs no flag to say so.
    /// </summary>
    [Fact]
    public void With_no_state_a_chorus_is_a_wire()
    {
        foreach (var (x, seen) in Painted(ChorusType, 0)) seen.ShouldBe(x, 1e-6f);
        foreach (var (x, seen) in Painted(ChorusType, 1)) seen.ShouldBe(x, 1e-6f);
    }

    // --- the flanger -----------------------------------------------------------

    /// <summary>
    /// The comb: a delay of d reinforces whatever has a period of d and cancels
    /// whatever has a period of twice it. Held still by turning the depth off, so
    /// the delay stays at its centre of 2.7 ms — a 370 Hz tone comes back in step
    /// and a 185 Hz one comes back half a cycle out.
    /// </summary>
    /// <remarks>
    /// At an even mix, and not at a full one. A comb is the sum of two signals,
    /// so a flanger turned fully wet has nothing left to cancel against and is
    /// simply a delayed tone at full level — which is the first thing this test
    /// caught.
    /// </remarks>
    [Fact]
    public void A_flanger_cancels_and_reinforces_at_the_delay_it_is_set_to()
    {
        var reinforced = Through(FlangerType, Tone(370f, 24_000), 0, (Depth, 0f), (3, 0f), (4, 0.5f));
        var cancelled = Through(FlangerType, Tone(185f, 24_000), 0, (Depth, 0f), (3, 0f), (4, 0.5f));

        Peak(reinforced, 12_000, 24_000).ShouldBeGreaterThan(0.9f);
        Peak(cancelled, 12_000, 24_000).ShouldBeLessThan(0.1f);
    }

    /// <summary>
    /// Feedback is signed, and the sign is not a nicety: it moves the notches to
    /// where the peaks were, which is a different effect rather than more of the
    /// same one.
    /// </summary>
    [Fact]
    public void The_sign_of_the_feedback_turns_the_comb_over()
    {
        var tone = Tone(370f, 24_000);

        var positive = Peak(Through(FlangerType, tone, 0, (Depth, 0f), (3, 0.8f), (4, 0.5f)), 12_000, 24_000);
        var negative = Peak(Through(FlangerType, tone, 0, (Depth, 0f), (3, -0.8f), (4, 0.5f)), 12_000, 24_000);

        // The tone the positive setting reinforces is the one the negative
        // setting is busy cancelling.
        positive.ShouldBeGreaterThan(negative * 3f);
    }

    [Fact]
    public void A_flanger_stays_bounded_at_any_feedback_it_offers()
    {
        foreach (var feedback in new[] { -0.95f, -0.5f, 0f, 0.5f, 0.95f, 4f, -4f })
        {
            var output = Through(FlangerType, Noise(20_000), 0, (3, feedback), (4, 1f));

            foreach (var sample in output)
            {
                float.IsFinite(sample).ShouldBeTrue();
                MathF.Abs(sample).ShouldBeLessThan(16f);
            }
        }
    }

    [Fact]
    public void With_no_state_a_flanger_is_a_wire()
    {
        foreach (var (x, seen) in Painted(FlangerType, 0)) seen.ShouldBe(x, 1e-6f);
    }

    // --- the phaser ------------------------------------------------------------

    /// <summary>
    /// Four stages and the loop round them, plus the two cells the emitter keeps
    /// for everything — the clock and the flag. No delay lines at all, which is
    /// the point: this is the module that would have needed an opcode.
    /// </summary>
    [Fact]
    public void A_phaser_is_five_cells_and_no_buffer()
    {
        var program = Audio(PhaserType, 0);

        program.DelayLengths.ShouldBeEmpty();
        program.UnitCount.ShouldBe(7);
    }

    /// <summary>
    /// An allpass chain leaves the level alone and moves only the phase, so what
    /// comes out of the stages on their own is as loud as what went in. It is
    /// adding that back to the dry signal that makes the notches.
    /// </summary>
    [Fact]
    public void The_stages_pass_every_level_through_and_only_move_the_phase()
    {
        // Below the notches, where four stages have turned the phase most of the
        // way round but not all of it. Right at the corner they turn it a full
        // cycle between them and the output lines up with the input again, which
        // is true and would make a poor test of anything.
        var tone = Tone(400f, 24_000);

        // Full wet and no feedback: the dry signal is gone and what is left is
        // the chain alone.
        var wet = Through(PhaserType, tone, 0, (Depth, 0f), (3, 0f), (4, 1f));

        Peak(wet, 12_000, 24_000).ShouldBe(1f, 0.05f);

        // And it is not the input: the phase has moved, so the two do not line up.
        var apart = 0f;
        for (var i = 12_000; i < 24_000; i++) apart = MathF.Max(apart, MathF.Abs(wet[i] - tone[i]));

        apart.ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// The notch itself, held still by turning the depth off: at half mix the dry
    /// and the shifted copy cancel wherever they have ended up half a cycle apart,
    /// and pass wherever they have not.
    /// </summary>
    [Fact]
    public void A_phaser_notches_some_frequencies_and_not_others()
    {
        var levels = new List<float>();

        foreach (var hz in new[] { 60f, 200f, 500f, 1_200f, 3_000f, 8_000f })
        {
            var output = Through(PhaserType, Tone(hz, 24_000), 0, (Depth, 0f), (3, 0.7f), (4, 0.5f));
            levels.Add(Peak(output, 12_000, 24_000));
        }

        // Somewhere it cancels hard and somewhere it does not, which is what a
        // notch is and what a plain filter would not do.
        levels.Min().ShouldBeLessThan(0.35f);
        levels.Max().ShouldBeGreaterThan(0.8f);
    }

    [Fact]
    public void A_phaser_stays_bounded_at_any_feedback_it_offers()
    {
        foreach (var feedback in new[] { 0f, 0.5f, 0.9f, 4f, -4f })
        foreach (var depth in new[] { 0f, 1f })
        {
            var output = Through(PhaserType, Noise(20_000), 0, (Depth, depth), (3, feedback), (4, 1f));

            foreach (var sample in output)
            {
                float.IsFinite(sample).ShouldBeTrue();
                MathF.Abs(sample).ShouldBeLessThan(16f);
            }
        }
    }

    /// <summary>
    /// Unlike the other two this one cannot fall back by accident — its stages
    /// are not delay lines and do not become wires on their own — so it says what
    /// it means with the emitter's flag and stands aside entirely.
    /// </summary>
    [Fact]
    public void With_no_state_a_phaser_is_a_wire()
    {
        foreach (var (x, seen) in Painted(PhaserType, 0, (4, 1f))) seen.ShouldBe(x, 1e-6f);
    }


    // --- the preset ------------------------------------------------------------
    /// <summary>
    /// The preset builds, compiles, and reaches the speakers and nothing else.
    /// </summary>
    /// <remarks>
    /// The video half is asserted as an absence now rather than as a cost. It
    /// used to send each module's own LFO to a hue, a saturation and a Translate,
    /// and this test measured what that cost the picture: dead code is eliminated
    /// a module at a time rather than an op at a time, so a frame that wanted the
    /// sweep got the delay lines and the cells beside it. Inert rather than
    /// wasteful, but paid for — and paid for to draw the control signal rather
    /// than the effect. With no picture in the patch the walk back from the
    /// Output's colour reaches nothing at all, which is what the counts below say.
    /// </remarks>
    [Fact]
    public void The_preset_builds_and_reaches_the_speakers_alone()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Moving parts").Build(loaded.Modules);

        var types = patch.Nodes.Select(n => n.TypeId).ToList();
        types.ShouldContain(ChorusType);
        types.ShouldContain(FlangerType);
        types.ShouldContain(PhaserType);

        var video = patch.CompileForVideo(loaded.Modules);
        video.Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // Three lines for the ear: the flanger's one and the chorus's two.
        audio.Program.DelayLengths.Count.ShouldBe(3);
        audio.Program.UnitCount.ShouldBe(7);

        // And none at all for the eye, because nothing is wired to the colour.
        video.Program.DelayLengths.ShouldBeEmpty();
        video.Program.UnitCount.ShouldBe(0);
    }

    // --- harness ----------------------------------------------------------------

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
        var program = Video(typeId, port, knobs);
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

    /// <summary>The same, at one moment of the clock rather than one point of the input.</summary>
    private static float PaintedAt(
        string typeId, int port, float t, params (int Port, float Value)[] knobs)
    {
        var program = Video(typeId, port, knobs);
        var registers = program.AllocateRegisters();

        program.Evaluate(0f, 0f, t, registers, default);
        return (float)registers[program.OutputBase];
    }

    private static CompiledPatch Video(string typeId, int port, (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }

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

    /// <summary>
    /// How much the level of a steady tone moves over a stretch: the loudest
    /// short window in it against the quietest. Short enough that one window sees
    /// the sweep standing still, which is what makes the two ends comparable.
    /// </summary>
    private static float Spread(float[] signal, int from, int to)
    {
        const int window = 400;

        float loudest = 0f, quietest = float.MaxValue;

        for (var start = from; start + window <= to && start + window <= signal.Length; start += window)
        {
            var level = Peak(signal, start, start + window);

            loudest = MathF.Max(loudest, level);
            quietest = MathF.Min(quietest, level);
        }

        return loudest - quietest;
    }
}
