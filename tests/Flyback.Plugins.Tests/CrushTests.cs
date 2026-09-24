using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Crush module, fed through Coordinates' x one sample at a time with real
/// state behind it.
/// </summary>
public class CrushTests
{
    private const string CrushType = "flyback.voice.crush";

    private const int Bits = 1;
    private const int RatePort = 2;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_shaping()
    {
        var def = Catalog.Get(CrushType).ShouldNotBeNull();

        def.Name.ShouldBe("Crush");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        Catalog.ProviderOf(CrushType)!.Id.ShouldBe("flyback.voice");
    }

    [Theory]
    [InlineData(1f, 0.4f, 0f)]
    [InlineData(1f, 0.6f, 1f)]
    [InlineData(1f, -0.6f, -1f)]
    [InlineData(2f, 0.3f, 0.5f)]
    [InlineData(3f, 0.3f, 0.25f)]
    [InlineData(3f, -0.9f, -1f)]
    public void Bits_leave_that_many_levels_across_full_scale(float bits, float input, float expected)
    {
        Through([input], (Bits, bits), (RatePort, 2f * Rate))[0].ShouldBe(expected, 1e-6f);
    }

    [Fact]
    public void Sixteen_bits_at_the_full_rate_is_all_but_a_wire()
    {
        var signal = Tone(440f, 2_000);
        var output = Through(signal, (Bits, 16f), (RatePort, 2f * Rate));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1f / (1 << 15));
    }

    /// <summary>
    /// A sample is taken as often as 'rate' says and held in between, so the
    /// output is a staircase with a tread of the evaluation rate over 'rate'.
    /// </summary>
    [Fact]
    public void Rate_holds_each_sample_until_the_next_is_due()
    {
        var ramp = Ramp(0f, 0.99f, 480);
        var output = Through(ramp, (Bits, 16f), (RatePort, Rate / 8f));

        var changes = Enumerable.Range(1, output.Length - 1).Count(i => output[i] != output[i - 1]);
        changes.ShouldBeInRange(output.Length / 8 - 1, output.Length / 8 + 1);

        for (var i = 0; i < output.Length; i++)
            output[i].ShouldBeLessThanOrEqualTo(ramp[i] + 1e-4f, $"sample {i} holds a value not yet seen");
    }

    /// <summary>The first sample is taken at once, so a crushed voice does not start on nought.</summary>
    [Fact]
    public void The_first_evaluation_takes_a_sample()
    {
        Through([0.75f, 0f, 0f], (Bits, 16f), (RatePort, 50f))
            .ShouldAllBe(sample => MathF.Abs(sample - 0.75f) < 1e-4f);
    }

    [Fact]
    public void A_held_note_number_is_not_clamped_to_the_rails()
    {
        Through([60f, 64f, 67f], (Bits, 16f), (RatePort, 50f))[2].ShouldBe(60f, 1e-3f);
    }

    /// <summary>
    /// Holding below a tone's own pitch folds it back down as a new, lower one:
    /// a 3 kHz sine sampled at 4 kHz comes out a 1 kHz tone.
    /// </summary>
    [Fact]
    public void Holding_below_the_pitch_aliases_it_down()
    {
        var output = Through(Tone(3_000f, Rate), (Bits, 16f), (RatePort, 4_000f));

        Level(output, 1_000f).ShouldBeGreaterThan(Level(output, 3_000f) * 2f);
    }

    /// <summary>On the screen there is nothing to hold with, so only the levels are left.</summary>
    [Fact]
    public void On_the_picture_it_bands_and_does_not_hold()
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var crush = Add(patch, CrushType, (Bits, 2f), (RatePort, 50f));
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, crush.Id, 0);
        patch.Connect(crush.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var (x, expected) in new[] { (0.1f, 0f), (0.3f, 0.5f), (0.8f, 1f), (-0.3f, -0.5f) })
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            ((float)registers[program.OutputBase]).ShouldBe(expected, 1e-6f);
        }
    }

    private static float[] Through(float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var crush = Add(patch, CrushType, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(coord.Id, 0, crush.Id, 0);
        patch.Connect(crush.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

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

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static float[] Tone(float hz, int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(MathF.Tau * hz * i / Rate))];

    private static float[] Ramp(float from, float to, int length) =>
        [.. Enumerable.Range(0, length).Select(i => from + (to - from) * i / (length - 1f))];

    /// <summary>How much of <paramref name="hz"/> is in the signal: one bin of a DFT.</summary>
    private static float Level(float[] signal, float hz)
    {
        double re = 0, im = 0;
        for (var i = 0; i < signal.Length; i++)
        {
            var angle = Math.Tau * hz * i / Rate;
            re += signal[i] * Math.Cos(angle);
            im += signal[i] * Math.Sin(angle);
        }

        return (float)(Math.Sqrt(re * re + im * im) / signal.Length);
    }
}
