using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Slew module, driven sample by sample through Coordinates' x with real state behind it.
/// </summary>
public class SlewTests
{
    private const string SlewType = "flyback.voice.slew";

    private const int Rise = 1;
    private const int Fall = 2;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_timing_for_audio()
    {
        var def = Catalog.Get(SlewType).ShouldNotBeNull();

        def.Name.ShouldBe("Slew");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        Catalog.ProviderOf(SlewType)!.Id.ShouldBe("flyback.voice");
    }

    /// <summary>The shared clock and memory flag, and one cell of its own.</summary>
    [Fact]
    public void It_keeps_one_cell_of_its_own()
    {
        var patch = Wired(out _);
        patch.CompileForAudio(Catalog).Program.UnitCount.ShouldBe(3);
    }

    [Fact]
    public void It_starts_at_its_input_rather_than_gliding_up_from_nought()
    {
        Through(Hold(0.8f, 10))[0].ShouldBe(0.8f, 1e-6f);
    }

    [Fact]
    public void The_knob_is_the_time_to_get_within_one_per_cent()
    {
        // A tenth of a second, from 0 to 1.
        var output = Through(Jump(0f, 1f, Rate / 2), (Rise, -1f));
        var at = (double seconds) => output[Rate / 4 + (int)(seconds * Rate)];

        at(0.05).ShouldBe(0.9f, 0.01f);
        at(0.09).ShouldBeLessThan(0.99f);
        at(0.1).ShouldBe(0.99f, 0.002f);
        at(0.2).ShouldBe(1f, 0.001f);
    }

    [Fact]
    public void A_glide_takes_as_long_over_an_octave_as_over_a_semitone()
    {
        var semitone = Through(Jump(60f, 61f, Rate / 2), (Rise, -1f));
        var octave = Through(Jump(60f, 72f, Rate / 2), (Rise, -1f));

        var half = Rate / 4 + Rate / 20;
        ((semitone[half] - 60f) / 1f).ShouldBe((octave[half] - 60f) / 12f, 1e-3f);
    }

    /// <summary>A signal cell would clamp at 16 and pin every note to it.</summary>
    [Fact]
    public void It_holds_a_note_number_above_the_rails()
    {
        var output = Through(Jump(60f, 72f, Rate / 2), (Rise, -2f));

        output[Rate / 8].ShouldBe(60f, 1e-4f);
        output[^1].ShouldBe(72f, 1e-3f);
    }

    [Fact]
    public void Rise_and_fall_are_separate()
    {
        var up = Through(Jump(0f, 1f, Rate / 2), (Rise, -3f), (Fall, 0f));
        var down = Through(Jump(1f, 0f, Rate / 2), (Rise, -3f), (Fall, 0f));

        var soon = Rate / 4 + Rate / 100;
        up[soon].ShouldBe(1f, 1e-3f);
        down[soon].ShouldBeGreaterThan(0.9f);
    }

    [Fact]
    public void It_stays_between_its_inputs_whatever_the_time_is_set_to()
    {
        foreach (var time in new[] { -40f, -4f, 1.5f, 30f, float.NaN })
        {
            var output = Through(Jump(-3f, 20f, Rate / 10), (Rise, time), (Fall, time));

            output.ShouldAllBe(s => float.IsFinite(s) && s >= -3f - 1e-3f && s <= 20f + 1e-3f);
        }
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        var patch = Wired(out var sink, NodeCatalog.OutputColorPort, (Rise, 0f));
        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var x in new[] { -0.75f, 0f, 0.5f, 1f })
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            ((float)registers[program.OutputBase]).ShouldBe(x, 1e-6f);
        }
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Through(float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = Wired(out _, NodeCatalog.OutputLeftPort, knobs);
        var program = patch.CompileForAudio(Catalog).Program;

        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    private static Patch Wired(
        out NodeInstance sink, int into = NodeCatalog.OutputLeftPort, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = NodeInstance.Create(Catalog.Require("coord"), 0, 0);
        var slew = NodeInstance.Create(Catalog.Require(SlewType), 0, 0);
        foreach (var (port, value) in knobs) slew.InputValues[port] = value;

        sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputGainPort] = 1f;

        patch.Nodes.Add(coord);
        patch.Nodes.Add(slew);
        patch.Nodes.Add(sink);
        patch.Connect(coord.Id, 0, slew.Id, 0);
        patch.Connect(slew.Id, 0, sink.Id, into);

        return patch;
    }

    private static float[] Hold(float value, int length) => [.. Enumerable.Repeat(value, length)];

    /// <summary>A quarter of a second at <paramref name="from"/>, then <paramref name="to"/>.</summary>
    private static float[] Jump(float from, float to, int length) =>
        [.. Enumerable.Range(0, length).Select(i => i < Rate / 4 ? from : to)];
}
