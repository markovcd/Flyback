using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Delay and Reverb modules, loaded off disk and driven sample by sample
/// with real delay state behind them.
/// </summary>
/// <remarks>
/// The signal goes in through the Coordinates module's x, because
/// <see cref="CompiledPatch.Evaluate"/> takes x per evaluation — which makes it
/// the one way to feed a module an arbitrary waveform without building an
/// oscillator to produce it. Everything runs at 1 kHz so a delay in seconds is a
/// whole number of samples.
/// </remarks>
public class SpaceTests
{
    private const string DelayType = "flyback.space.delay";
    private const string ReverbType = "flyback.space.reverb";

    private const int Rate = 1_000;

    /// <summary>A line is read before it is written, so everything lands one sample late.</summary>
    private const int Lag = 1;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_plugin_offers_both_modules_from_one_assembly()
    {
        Catalog.Get(DelayType).ShouldNotBeNull().Name.ShouldBe("Delay");
        Catalog.Get(ReverbType).ShouldNotBeNull().Name.ShouldBe("Reverb");

        Catalog.ProviderOf(DelayType).ShouldBe(Catalog.ProviderOf(ReverbType));
    }

    [Fact]
    public void A_delay_at_no_mix_is_exactly_a_wire()
    {
        var signal = Noise(64);
        var output = Through(DelayType, signal, (1, 0.01f), (2, 0.7f), (3, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-5f);
    }

    [Fact]
    public void What_goes_into_a_delay_comes_back_out_later()
    {
        var output = Through(DelayType, Impulse(128), (1, 0.02f), (2, 0f), (3, 1f));

        output[20 + Lag].ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void Turning_feedback_up_gives_more_repeats()
    {
        var quiet = Through(DelayType, Impulse(256), (1, 0.02f), (2, 0.2f), (3, 1f));
        var loud = Through(DelayType, Impulse(256), (1, 0.02f), (2, 0.8f), (3, 1f));

        Energy(quiet, 100, 256).ShouldBeLessThan(Energy(loud, 100, 256));
    }

    /// <summary>Sweeping the delay time must glide, which is what interpolation is for.</summary>
    [Fact]
    public void A_delay_time_between_two_samples_lands_between_them()
    {
        var early = Through(DelayType, Impulse(128), (1, 0.020f), (2, 0f), (3, 1f));
        var late = Through(DelayType, Impulse(128), (1, 0.021f), (2, 0f), (3, 1f));
        var between = Through(DelayType, Impulse(128), (1, 0.0205f), (2, 0f), (3, 1f));

        // The whole impulse sits on one sample in each of the outer cases, and is
        // split across both in the middle one.
        early[20 + Lag].ShouldBe(1f, 1e-3f);
        late[21 + Lag].ShouldBe(1f, 1e-3f);

        between[20 + Lag].ShouldBe(0.5f, 0.05f);
        between[21 + Lag].ShouldBe(0.5f, 0.05f);
    }

    [Fact]
    public void A_reverb_at_no_mix_is_exactly_a_wire()
    {
        var signal = Noise(64);
        var output = Through(ReverbType, signal, (1, 0.8f), (2, 0.9f), (3, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-5f);
    }

    /// <summary>
    /// The thing that makes it a reverb rather than an echo: sound keeps arriving
    /// after the input has stopped, and it fades rather than repeating.
    /// </summary>
    [Fact]
    public void A_reverb_rings_on_after_the_input_stops()
    {
        var signal = new float[4_000];
        for (var i = 0; i < 20; i++) signal[i] = 1f;

        var output = Through(ReverbType, signal, (1, 0.5f), (2, 0.8f), (3, 1f));

        var early = Energy(output, 200, 1_000);
        var late = Energy(output, 1_000, 2_000);
        var latest = Energy(output, 3_000, 4_000);

        early.ShouldBeGreaterThan(0f);
        late.ShouldBeGreaterThan(0f);

        // Still going long after the input, and quieter every time you look.
        latest.ShouldBeLessThan(late);
        late.ShouldBeLessThan(early);
    }

    [Fact]
    public void A_longer_decay_rings_for_longer()
    {
        var signal = new float[4_000];
        for (var i = 0; i < 20; i++) signal[i] = 1f;

        var quick = Through(ReverbType, signal, (1, 0.5f), (2, 0.2f), (3, 1f));
        var slow = Through(ReverbType, signal, (1, 0.5f), (2, 0.95f), (3, 1f));

        Energy(quick, 2_000, 4_000).ShouldBeLessThan(Energy(slow, 2_000, 4_000));
    }

    /// <summary>
    /// A comb's gain at DC is one over one minus its feedback, so a long tail is
    /// also an enormous one unless the input is scaled by exactly that first.
    /// Sustained full-scale input is the case that would expose it.
    /// </summary>
    [Fact]
    public void A_reverb_does_not_get_louder_than_what_went_in()
    {
        var signal = new float[8_000];
        Array.Fill(signal, 1f);

        foreach (var decay in new[] { 0f, 0.5f, 0.95f, 1f })
        {
            var output = Through(ReverbType, signal, (1, 0.5f), (2, decay), (3, 1f));

            foreach (var sample in output)
            {
                float.IsFinite(sample).ShouldBeTrue();
                MathF.Abs(sample).ShouldBeLessThan(2f);
            }
        }
    }

    /// <summary>
    /// A Delay is a wire on the video path. There is no state there — rows render
    /// in parallel — so the op hands its input straight on, and a patch written
    /// for the speakers still draws what it drew before.
    /// </summary>
    [Fact]
    public void With_no_state_a_delay_passes_the_picture_through()
    {
        foreach (var (x, seen) in Painted(DelayType))
            seen.ShouldBe(x, 1e-5f);
    }

    /// <summary>
    /// A Reverb cannot manage the same trick, and this pins how far off it is. A
    /// comb's gain at DC is one over one minus its feedback, so the wet path is
    /// scaled by exactly that to come out at unity — and with no state that
    /// scaling is the only thing left, so the picture dims by it. What matters is
    /// that it stays proportional and never reaches black: a module that silently
    /// zeroed a video patch would be far worse than one that darkens it.
    /// </summary>
    [Fact]
    public void With_no_state_a_reverb_dims_the_picture_rather_than_killing_it()
    {
        var painted = Painted(ReverbType);
        var scale = painted[1].Seen / painted[1].X;

        scale.ShouldBeInRange(0.1f, 1f);

        foreach (var (x, seen) in painted)
            seen.ShouldBe(x * scale, 1e-5f);
    }

    /// <summary>Compiles the module into the video sink and reads it with no state, as SynthRenderer does.</summary>
    private static (float X, float Seen)[] Painted(string typeId)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, (3, 1f));
        var screen = Add(patch, NodeCatalog.VideoOutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, screen.Id, 0);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        return
        [
            .. new[] { 0f, 0.25f, 0.5f, 1f }.Select(x =>
            {
                program.Evaluate(x, 0f, 0f, registers, default);
                return (x, (float)registers[program.OutputBase]);
            }),
        ];
    }

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Echo chamber").Build(loaded.Modules);

        patch.Nodes.Select(n => n.TypeId).ShouldContain(DelayType);
        patch.Nodes.Select(n => n.TypeId).ShouldContain(ReverbType);

        patch.CompileForVideo(loaded.Modules).Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // Two delay lines for the echo and the reverb's six, in program order.
        audio.Program.DelayLengths.Count.ShouldBe(7);
    }

    // --- harness ----------------------------------------------------------------

    /// <summary>
    /// Feeds a signal through one module into the audio sink, one sample at a
    /// time, with the delay state the program asked for.
    /// </summary>
    private static float[] Through(string typeId, float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, knobs);
        var sink = Add(patch, NodeCatalog.AudioOutputTypeId, (2, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, sink.Id, 0);

        var program = patch.CompileForAudio(Catalog).Program;
        var delays = new DelayState(program.DelayLengths, Rate);
        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)Rate, registers, default, delays);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static float[] Impulse(int length)
    {
        var signal = new float[length];
        signal[0] = 1f;
        return signal;
    }

    /// <summary>Deterministic, so a failure is reproducible.</summary>
    private static float[] Noise(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(i * 12.9898f) * 0.5f)];

    private static float Energy(float[] signal, int from, int to)
    {
        var sum = 0f;
        for (var i = from; i < to && i < signal.Length; i++) sum += signal[i] * signal[i];
        return sum;
    }
}
