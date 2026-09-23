using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Hiss, run a sample at a time with its level on a wire. It has a Filter in it,
/// so it needs the state a renderer would give it.
/// </summary>
public class HissTests
{
    private const string HissType = "flyback.voice.hiss";

    private const int LevelPort = 1;
    private const int CutoffPort = 2;
    private const int ResonancePort = 3;
    private const int GainPort = 4;
    private const int SeedPort = 5;

    private const int Rate = 48_000;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_drum()
    {
        var def = Catalog.Get(HissType).ShouldNotBeNull();

        def.Name.ShouldBe("Hiss");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Extra<SettingsExtra>().ShouldNotBeNull().Fields.Select(f => f.Key).ShouldBe(["noise", "band"]);
        Catalog.ProviderOf(HissType)!.Id.ShouldBe("flyback.voice");
    }

    [Fact]
    public void With_no_level_it_is_silent()
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Played(Held(0f), "white", "high").ShouldAllBe(s => s == 0d);
    }

    [Fact]
    public void The_high_band_is_brighter_than_the_low_one()
    {
        var high = Played(Held(1f, 9_600), "white", "high", (CutoffPort, 4000f));
        var low = Played(Held(1f, 9_600), "white", "low", (CutoffPort, 400f));

        Crossings(high).ShouldBeGreaterThan(Crossings(low) * 4);
    }

    /// <summary>
    /// A Random, a Filter and the two Multiplies that play and level it, against
    /// this, for each noise and each band. Equal rather than close, and with the
    /// Random shared with nothing, which is what a preset moved onto this gives up:
    /// the noise has no memory, so a copy of it is the same samples.
    /// </summary>
    [Theory]
    [InlineData("white", "high", 0, 2)]
    [InlineData("white", "band", 0, 1)]
    [InlineData("white", "low", 0, 0)]
    [InlineData("pink", "band", 1, 1)]
    public void It_is_the_modules_it_stands_for_to_the_last_bit(string noise, string band, int noiseOut, int bandOut)
    {
        var b = new PatchBuilder(Catalog);
        var level = b.Add("coord");
        var random = b.Add(NodeCatalog.RandomTypeId, (2, 3f));
        var filter = b.Add(NodeCatalog.FilterTypeId, (1, 1900f), (2, 0.3f));
        var played = b.Add("math.mul");
        var leveled = b.Add("math.mul", (1, 2.5f));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(random, noiseOut, filter, 0)
         .Wire(level, 0, played, 0).Wire(filter, bandOut, played, 1)
         .Wire(played, 0, leveled, 0)
         .Wire(leveled, 0, sink, NodeCatalog.OutputLeftPort);

        var envelope = Falling(Rate / 4);

        Run(b.Patch, envelope).ShouldBe(
            Played(envelope, noise, band, (CutoffPort, 1900f), (ResonancePort, 0.3f), (GainPort, 2.5f), (SeedPort, 3f)));
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Held(float level, int length = 2_400) =>
        [.. Enumerable.Repeat(level, length)];

    /// <summary>One to nought in a straight line, cubed — a stroke.</summary>
    private static float[] Falling(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Pow(1f - i / (float)length, 3f))];

    private static double[] Played(float[] level, string noise, string band, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add("coord");
        var hiss = b.Add(HissType, knobs);
        hiss.SetState("hiss", new JsonObject { ["noise"] = noise, ["band"] = band });
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, hiss, LevelPort).Wire(hiss, 0, sink, NodeCatalog.OutputLeftPort);

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
