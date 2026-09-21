using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>The Delay module, driven sample by sample with real delay state behind it.</summary>
/// <remarks>
/// The signal goes in through Coordinates' x, because
/// <see cref="CompiledPatch.Evaluate"/> takes x per evaluation — the one way
/// to feed a module an arbitrary waveform. Everything runs at 1 kHz, so a delay in
/// seconds is a whole number of samples.
/// </remarks>
public class DelayTests
{
    private const int Rate = 1_000;

    /// <summary>A line is read before it is written, so everything lands one sample late.</summary>
    private const int Lag = 1;

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    [Fact]
    public void It_is_offered_under_time_effects()
    {
        var def = Modules.Get(NodeCatalog.DelayTypeId).ShouldNotBeNull();

        def.Name.ShouldBe("Delay");
        def.Category.ShouldBe(ModuleCategories.TimeEffects);
    }

    [Fact]
    public void A_delay_at_no_mix_is_exactly_a_wire()
    {
        var signal = Noise(64);
        var output = Through(signal, (1, 0.01f), (2, 0.7f), (3, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-5f);
    }

    [Fact]
    public void What_goes_into_a_delay_comes_back_out_later()
    {
        var output = Through(Impulse(128), (1, 0.02f), (2, 0f), (3, 1f));

        output[20 + Lag].ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void Turning_feedback_up_gives_more_repeats()
    {
        var quiet = Through(Impulse(256), (1, 0.02f), (2, 0.2f), (3, 1f));
        var loud = Through(Impulse(256), (1, 0.02f), (2, 0.8f), (3, 1f));

        Energy(quiet, 100, 256).ShouldBeLessThan(Energy(loud, 100, 256));
    }

    /// <summary>Sweeping the delay time must glide, which is what interpolation is for.</summary>
    [Fact]
    public void A_delay_time_between_two_samples_lands_between_them()
    {
        var early = Through(Impulse(128), (1, 0.020f), (2, 0f), (3, 1f));
        var late = Through(Impulse(128), (1, 0.021f), (2, 0f), (3, 1f));
        var between = Through(Impulse(128), (1, 0.0205f), (2, 0f), (3, 1f));

        // The whole impulse sits on one sample in each of the outer cases, and is
        // split across both in the middle one.
        early[20 + Lag].ShouldBe(1f, 1e-3f);
        late[21 + Lag].ShouldBe(1f, 1e-3f);

        between[20 + Lag].ShouldBe(0.5f, 0.05f);
        between[21 + Lag].ShouldBe(0.5f, 0.05f);
    }

    /// <summary>
    /// A Delay is a wire on the video path. There is no state there — rows render
    /// in parallel — so the op hands its input straight on, and a patch written
    /// for the speakers still draws what it drew before.
    /// </summary>
    [Fact]
    public void With_no_state_a_delay_passes_the_picture_through()
    {
        foreach (var (x, seen) in Painted())
            seen.ShouldBe(x, 1e-5f);
    }

    // --- harness ----------------------------------------------------------------

    /// <summary>
    /// Feeds a signal through one Delay into the audio sink, one sample at a time,
    /// with the delay state the program asked for.
    /// </summary>
    private static float[] Through(float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.DelayTypeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Modules).Program;
        var delays = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
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
    private static (float X, float Seen)[] Painted()
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, NodeCatalog.DelayTypeId, (3, 1f));
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, screen.Id, 0);

        var program = patch.CompileForVideo(Modules).Program;
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

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Modules.Require(typeId), 0, 0);

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
