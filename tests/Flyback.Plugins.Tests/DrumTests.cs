using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Drum, run a sample at a time with its level on a wire. It carries an
/// oscillator's phase, so it needs the state a renderer would give it.
/// </summary>
public class DrumTests
{
    private const string DrumType = "flyback.voice.drum";

    private const int LevelPort = 1;
    private const int PitchPort = 2;
    private const int SweepPort = 3;
    private const int BendPort = 4;
    private const int DrivePort = 5;

    private const int Rate = 48_000;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_oscillators()
    {
        var def = Catalog.Get(DrumType).ShouldNotBeNull();

        def.Name.ShouldBe("Drum");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Inputs[0].Domain.ShouldBeTrue();
        Catalog.ProviderOf(DrumType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "drum").ShouldBe(1);
    }

    [Fact]
    public void With_no_level_it_is_silent()
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Played(Held(0f), (DrivePort, 4f)).ShouldAllBe(s => s == 0d);
    }

    [Fact]
    public void At_a_low_level_it_rings_at_its_pitch()
    {
        // A level of a tenth to the fourth is a ten-thousandth of the sweep.
        var rung = Played(Held(0.1f, Rate), (PitchPort, 60f), (SweepPort, 100f), (DrivePort, 0f));

        Crossings(rung).ShouldBeInRange(118, 122);
    }

    [Fact]
    public void A_hit_starts_high_and_falls_to_its_pitch()
    {
        var hit = Played(Falling(Rate), (PitchPort, 50f), (SweepPort, 400f), (BendPort, 2f), (DrivePort, 0f));

        var early = Crossings(hit.AsSpan(0, Rate / 10).ToArray());
        var late = Crossings(hit.AsSpan(Rate * 6 / 10, Rate / 10).ToArray());

        early.ShouldBeGreaterThan(late * 2);
    }

    [Fact]
    public void Drive_thickens_it_and_never_makes_it_louder()
    {
        var clean = Played(Held(0.8f, 4_800), (DrivePort, 0f));
        var driven = Played(Held(0.8f, 4_800), (DrivePort, 8f));

        driven.Max(Math.Abs).ShouldBeLessThanOrEqualTo(1d);
        driven.Average(Math.Abs).ShouldBeGreaterThan(clean.Average(Math.Abs));
    }

    /// <summary>
    /// A Power, a Remap, a Sine with its frequency and its level on wires, and a
    /// Drive, against this. Equal rather than close, so a kick moved onto it is the
    /// kick it was.
    /// </summary>
    [Theory]
    [InlineData(2.5f)]
    [InlineData(0f)]
    public void It_is_the_modules_it_stands_for_to_the_last_bit(float drive)
    {
        var b = new PatchBuilder(Catalog);
        var level = b.Add("coord");
        var bent = b.Add("math.pow", (1, 4f));
        var pitch = b.Add("math.remap", (1, 0f), (2, 1f), (3, 45f), (4, 170f));
        var tone = b.Add("osc.sine");
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(level, 0, bent, 0).Wire(bent, 0, pitch, 0).Wire(pitch, 0, tone, 1).Wire(level, 0, tone, 3);

        if (drive > 0f)
        {
            var driven = b.Add(NodeCatalog.DriveTypeId, (1, drive));
            b.Wire(tone, 0, driven, 0).Wire(driven, 0, sink, NodeCatalog.OutputLeftPort);
        }
        else
        {
            b.Wire(tone, 0, sink, NodeCatalog.OutputLeftPort);
        }

        var envelope = Falling(Rate / 2);

        Run(b.Patch, envelope).ShouldBe(
            Played(envelope, (PitchPort, 45f), (SweepPort, 125f), (BendPort, 4f), (DrivePort, drive)));
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
        var drum = b.Add(DrumType, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, 0, drum, LevelPort).Wire(drum, 0, sink, NodeCatalog.OutputLeftPort);

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
