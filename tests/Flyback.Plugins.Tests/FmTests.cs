using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The FM, run a sample at a time with its level on a wire. It carries four
/// oscillators' phases, so it needs the state a renderer would give it.
/// </summary>
public class FmTests
{
    private const string FmType = "flyback.voice.fm";

    private const int FreqPort = 1;
    private const int LevelPort = 2;
    private const int TonePort = 3;
    private const int Ratio2Port = 4;
    private const int Ratio3Port = 5;
    private const int Ratio4Port = 6;
    private const int Index2Port = 7;
    private const int Index3Port = 8;
    private const int Index4Port = 9;

    private const int Rate = 48_000;

    private static readonly string[] Algorithms = ["stack", "branch", "fan", "pair", "organ"];

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_oscillators()
    {
        var def = Catalog.Get(FmType).ShouldNotBeNull();

        def.Name.ShouldBe("FM");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Inputs[0].Domain.ShouldBeTrue();
        def.Extra<SettingsExtra>().ShouldNotBeNull().Fields.Select(f => f.Key).ShouldBe(["algorithm"]);
        Catalog.ProviderOf(FmType)!.Id.ShouldBe("flyback.voice");
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("branch")]
    [InlineData("fan")]
    [InlineData("pair")]
    [InlineData("organ")]
    public void With_no_level_it_is_silent(string algorithm)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Played(Held(0f), algorithm).ShouldAllBe(s => s == 0d);
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("branch")]
    [InlineData("fan")]
    public void With_no_tone_one_carrier_is_a_sine_at_its_pitch(string algorithm)
    {
        var rung = Played(Held(0.5f, Rate), algorithm, (FreqPort, 200f), (TonePort, 0f));

        Crossings(rung).ShouldBeInRange(398, 402);
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("branch")]
    [InlineData("fan")]
    [InlineData("pair")]
    [InlineData("organ")]
    public void However_it_is_wired_it_peaks_at_its_level(string algorithm)
    {
        var rung = Played(Held(0.5f, Rate / 4), algorithm, (Index2Port, 2f), (Index3Port, 2f), (Index4Port, 2f));

        rung.Max(Math.Abs).ShouldBeLessThanOrEqualTo(0.5d + 1e-6);
        rung.Max(Math.Abs).ShouldBeGreaterThan(0.2d);
    }

    [Fact]
    public void Every_algorithm_is_a_different_sound()
    {
        var played = Algorithms
            .Select(a => Played(Held(0.8f, Rate / 10), a, (Index2Port, 0.7f), (Index3Port, 0.5f), (Index4Port, 0.3f)))
            .ToArray();

        for (var i = 0; i < played.Length; i++)
            for (var j = i + 1; j < played.Length; j++)
                played[i].ShouldNotBe(played[j], $"{Algorithms[i]} and {Algorithms[j]}");
    }

    [Fact]
    public void The_modulators_are_there_when_it_is_struck_and_gone_once_it_has_rung()
    {
        var struck = Played(Falling(Rate), "stack", (FreqPort, 200f), (Index2Port, 1.2f), (Index3Port, 0.8f));

        var early = Crossings(struck.AsSpan(0, Rate / 10).ToArray());
        var late = Crossings(struck.AsSpan(Rate * 8 / 10, Rate / 10).ToArray());

        early.ShouldBeGreaterThan(late);
    }

    /// <summary>
    /// Operator two alone on operator one is the two-sine Bell, and equal rather than
    /// close, in each algorithm where two bends one.
    /// </summary>
    [Theory]
    [InlineData("stack", 2.76f, 0.3f)]
    [InlineData("branch", 1.41f, 0.6f)]
    [InlineData("fan", 3.5f, 1.2f)]
    public void With_two_operators_it_is_a_bell_to_the_last_bit(string algorithm, float ratio, float index)
    {
        var b = new PatchBuilder(Catalog);
        var level = b.Add("coord");
        var bell = b.Add("flyback.voice.bell", (FreqPort, 277.2f), (3, ratio), (4, index));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(level, 0, bell, LevelPort).Wire(bell, 0, sink, NodeCatalog.OutputLeftPort);

        var envelope = Falling(Rate / 2);

        Run(b.Patch, envelope).ShouldBe(
            Played(envelope, algorithm, (FreqPort, 277.2f), (Ratio2Port, ratio), (Index2Port, index),
                (Ratio3Port, 5f), (Index3Port, 0f), (Ratio4Port, 7f), (Index4Port, 0f)));
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Held(float level, int length = 2_400) =>
        [.. Enumerable.Repeat(level, length)];

    /// <summary>One to nought in a straight line, cubed — a stroke.</summary>
    private static float[] Falling(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Pow(1f - i / (float)length, 3f))];

    private static double[] Played(float[] level, string algorithm, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add("coord");
        var fm = b.Add(FmType, knobs);
        fm.SetState("fm", new JsonObject { ["algorithm"] = algorithm });
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, fm, LevelPort).Wire(fm, 0, sink, NodeCatalog.OutputLeftPort);

        return Run(b.Patch, level);
    }

    /// <summary>The level arrives as x, which is the one input a test can vary per sample.</summary>
    private static double[] Run(Patch patch, float[] level)
    {
        var program = patch.CompileForAudio(Catalog).Program;
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var output = new double[level.Length];

        for (var i = 0; i < level.Length; i++)
        {
            program.Evaluate(level[i], 0f, i / (double)Rate, registers, default, state);
            output[i] = registers[program.OutputBase];
        }

        return output;
    }

    private static int Crossings(double[] signal)
    {
        var crossings = 0;
        for (var i = 1; i < signal.Length; i++)
            if (signal[i - 1] < 0d != signal[i] < 0d) crossings++;
        return crossings;
    }
}
