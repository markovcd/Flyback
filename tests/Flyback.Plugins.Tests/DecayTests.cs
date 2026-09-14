using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Decay envelope, triggered through Coordinates' x with real state behind it.
/// </summary>
public class DecayTests
{
    private const string DecayType = "flyback.voice.decay";

    private const int Attack = 1;
    private const int DecayPort = 2;
    private const int Curve = 3;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_timing_for_audio()
    {
        var def = Catalog.Get(DecayType).ShouldNotBeNull();

        def.Name.ShouldBe("Decay");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        Catalog.ProviderOf(DecayType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "decay").ShouldBe(1);
    }

    /// <summary>The shared clock and memory flag, plus level, stage and the trigger as it was.</summary>
    [Fact]
    public void It_keeps_three_cells_of_its_own()
    {
        Wired().CompileForAudio(Catalog).Program.UnitCount.ShouldBe(5);
    }

    [Fact]
    public void A_single_sample_pulse_plays_the_whole_envelope_on_time()
    {
        // 10 ms up, 100 ms down, in straight lines.
        var output = Through(Pulse(1, Rate / 2), (Attack, -2f), (DecayPort, -1f), (Curve, 0f));

        output[Rate / 200].ShouldBe(0.5f, 0.01f);
        output[Rate / 100].ShouldBe(1f, 0.01f);
        output[Rate / 100 + Rate / 20].ShouldBe(0.5f, 0.01f);
        output[Rate / 100 + Rate / 10 + 2].ShouldBe(0f);
    }

    [Fact]
    public void It_falls_without_waiting_for_the_trigger_to_close()
    {
        var output = Through(Pulse(Rate, Rate), (Attack, -3f), (DecayPort, -1f));

        output[Rate / 2].ShouldBe(0f);
    }

    [Fact]
    public void A_held_trigger_strikes_once()
    {
        var output = Through(Pulse(Rate, Rate), (Attack, -3f), (DecayPort, -1f));

        output[(Rate / 5)..].ShouldAllBe(s => s == 0f);
    }

    [Fact]
    public void The_curve_drops_fast_and_still_ends_on_time()
    {
        var output = Through(Pulse(1, Rate / 2), (Attack, -4f), (DecayPort, -1f), (Curve, 1f));

        output[Rate / 20].ShouldBe(0.0625f, 0.01f);
        output[Rate / 10 + 10].ShouldBe(0f, 1e-6f);
    }

    [Fact]
    public void A_hit_mid_fall_rises_from_where_it_is()
    {
        var trigger = new float[Rate / 2];
        trigger[0] = 1f;
        trigger[Rate / 20] = 1f;

        var output = Through(trigger, (Attack, -2f), (DecayPort, -1f), (Curve, 0f));
        var again = Rate / 20;

        output[again - 1].ShouldBeGreaterThan(0.3f);
        output[again].ShouldBeGreaterThanOrEqualTo(output[again - 1]);

        // From about 0.6 the attack has 0.4 left to climb, which at 10 ms for the whole way is 4 ms.
        output[again + Rate / 250 + 1].ShouldBe(1f, 0.01f);
    }

    [Fact]
    public void It_stays_between_silence_and_full_whatever_the_times_are()
    {
        foreach (var time in new[] { -40f, -4f, 1.5f, 30f, float.NaN })
        {
            var output = Through(Pulse(1, Rate / 10), (Attack, time), (DecayPort, time), (Curve, time));
            output.ShouldAllBe(s => float.IsFinite(s) && s >= 0f && s <= 1f);
        }
    }

    [Fact]
    public void On_the_picture_it_passes_the_trigger_through()
    {
        var program = Wired(NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var x in new[] { 0f, 0.25f, 1f })
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            ((float)registers[program.OutputBase]).ShouldBe(x, 1e-6f);
        }
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Through(float[] trigger, params (int Port, float Value)[] knobs)
    {
        var program = Wired(NodeCatalog.OutputLeftPort, knobs).CompileForAudio(Catalog).Program;

        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var output = new float[trigger.Length];

        for (var i = 0; i < trigger.Length; i++)
        {
            program.Evaluate(trigger[i], 0f, i / (double)Rate, registers, default, state);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    private static Patch Wired(int into = NodeCatalog.OutputLeftPort, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = NodeInstance.Create(Catalog.Require("coord"), 0, 0);
        var decay = NodeInstance.Create(Catalog.Require(DecayType), 0, 0);
        foreach (var (port, value) in knobs) decay.InputValues[port] = value;

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputGainPort] = 1f;

        patch.Nodes.Add(coord);
        patch.Nodes.Add(decay);
        patch.Nodes.Add(sink);
        patch.Connect(coord.Id, 0, decay.Id, 0);
        patch.Connect(decay.Id, 0, sink.Id, into);

        return patch;
    }

    /// <summary>High for the first <paramref name="high"/> samples, then low.</summary>
    private static float[] Pulse(int high, int length) =>
        [.. Enumerable.Range(0, length).Select(i => i < high ? 1f : 0f)];
}
