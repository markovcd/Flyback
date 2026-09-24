using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Bell, run a sample at a time with its level on a wire. It carries two
/// oscillators' phases, so it needs the state a renderer would give it.
/// </summary>
public class BellTests
{
    private const string BellType = "flyback.voice.bell";

    private const int FreqPort = 1;
    private const int LevelPort = 2;
    private const int RatioPort = 3;
    private const int IndexPort = 4;

    private const int Rate = 48_000;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_oscillators()
    {
        var def = Catalog.Get(BellType).ShouldNotBeNull();

        def.Name.ShouldBe("Bell");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Inputs[0].Domain.ShouldBeTrue();
        Catalog.ProviderOf(BellType)!.Id.ShouldBe("flyback.voice");
    }

    [Fact]
    public void With_no_level_it_is_silent()
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Played(Held(0f)).ShouldAllBe(s => s == 0d);
    }

    [Fact]
    public void With_no_index_it_is_a_sine_at_its_pitch()
    {
        var rung = Played(Held(0.5f, Rate), (FreqPort, 200f), (IndexPort, 0f));

        Crossings(rung).ShouldBeInRange(398, 402);
    }

    [Fact]
    public void The_overtone_is_there_when_it_is_struck_and_gone_once_it_has_rung()
    {
        var struck = Played(Falling(Rate), (FreqPort, 200f), (RatioPort, 2.76f), (IndexPort, 1.2f));

        var early = Crossings(struck.AsSpan(0, Rate / 10).ToArray());
        var late = Crossings(struck.AsSpan(Rate * 8 / 10, Rate / 10).ToArray());

        early.ShouldBeGreaterThan(late);
    }

    /// <summary>
    /// Two Sines, the upper on the lower's phase, and the two Multiplies that set
    /// the upper one's pitch and level, against this. Equal rather than close, so
    /// a gong moved onto it is the gong it was.
    /// </summary>
    [Theory]
    [InlineData(2.76f, 0.3f)]
    [InlineData(1.41f, 0.6f)]
    [InlineData(3.5f, 0f)]
    public void It_is_the_modules_it_stands_for_to_the_last_bit(float ratio, float index)
    {
        var b = new PatchBuilder(Catalog);
        var level = b.Add("coord");
        var hz = b.Add(NodeCatalog.ValueTypeId, (0, 277.2f));
        var above = b.Add("math.mul", (1, ratio));
        var lean = b.Add("math.mul", (1, index));
        var partial = b.Add("osc.sine");
        var bell = b.Add("osc.sine");
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(hz, 0, above, 0).Wire(level, 0, lean, 0)
         .Wire(above, 0, partial, 1).Wire(lean, 0, partial, 3)
         .Wire(hz, 0, bell, 1).Wire(level, 0, bell, 3).Wire(partial, 0, bell, 2)
         .Wire(bell, 0, sink, NodeCatalog.OutputLeftPort);

        var envelope = Falling(Rate / 2);

        Run(b.Patch, envelope).ShouldBe(
            Played(envelope, (FreqPort, 277.2f), (RatioPort, ratio), (IndexPort, index)));
    }

    // --- harness -----------------------------------------------------------------

    private static float[] Held(float level, int length = 2_400) =>
        [.. Enumerable.Repeat(level, length)];

    /// <summary>One to nought in a straight line, cubed — a stroke.</summary>
    private static float[] Falling(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Pow(1f - i / (float)length, 3f))];

    private static double[] Played(float[] level, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add("coord");
        var bell = b.Add(BellType, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, bell, LevelPort).Wire(bell, 0, sink, NodeCatalog.OutputLeftPort);

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
