using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Fold module, loaded off disk and driven sample by sample with real state
/// behind it, and the "Filter sweep" preset that pairs it with the engine's own
/// Filter.
/// </summary>
/// <remarks>
/// Signals go in through Coordinates' x, the way <see cref="SpaceTests"/> does it.
/// </remarks>
public class TimbreTests
{
    private const string FilterType = NodeCatalog.FilterTypeId;
    private const string FoldType = "flyback.voice.fold";

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void Fold_is_the_plugins_own_while_filter_is_the_engines()
    {
        Catalog.Get(FoldType).ShouldNotBeNull().Name.ShouldBe("Fold");
        Catalog.Get(FilterType).ShouldNotBeNull().Name.ShouldBe("Filter");

        Catalog.ProviderOf(FoldType)!.Id.ShouldBe("flyback.voice");
        Catalog.ProviderOf(FilterType)!.Id.ShouldBe(NodeCatalog.BuiltInProvider.Id);
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
        var screen = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

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

    // --- the preset ------------------------------------------------------------

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = ShippedPlugins.Loaded;
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
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

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

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// How much of a folded tone sits well above its own fundamental, measured
    /// through the Filter's highpass — the engine's own module, used here to take
    /// the measurement of the plugin's.
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

    private static float Energy(float[] signal, int from, int to)
    {
        var sum = 0f;
        for (var i = from; i < to && i < signal.Length; i++) sum += signal[i] * signal[i];
        return sum;
    }
}
