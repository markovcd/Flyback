using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Figures;

namespace Flyback.Plugins.Tests;

/// <summary>A struck plate rings and shows its sand figure from one strike.</summary>
public class PlateTests
{
    private const int Trigger = 0, Freq = 2, Aspect = 3, Decay = 4, StrikeX = 6, StrikeY = 7;
    private const int Out = 0, Figure = 1, Motion = 2;

    [Fact]
    public void Figures_offers_it_for_both_sinks_with_its_own_face()
    {
        var def = Catalog.Get(Plate).ShouldNotBeNull();

        def.Name.ShouldBe("Plate");
        def.Category.ShouldBe("Figures");
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Skin.ShouldBeOfType<ModuleSkin.Artwork>().Panel.ShouldNotBeNull();
        Catalog.ProviderOf(Plate)!.Id.ShouldBe("flyback.figures");
    }

    [Fact]
    public void It_is_silent_until_it_is_struck()
    {
        var heard = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort), new float[Rate / 10]);

        heard.ShouldAllBe(s => s == 0f);
    }

    [Fact]
    public void A_strike_rings_and_dies_away()
    {
        var heard = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Decay, 0.5f)), Pulse(1, Rate * 2));

        var atStrike = Rms(heard.AsSpan(0, Rate / 20));
        var later = Rms(heard.AsSpan(Rate, Rate / 20));
        var atEnd = Rms(heard.AsSpan(Rate * 2 - Rate / 20));

        atStrike.ShouldBeGreaterThan(0.05);
        later.ShouldBeLessThan(atStrike * 0.2);
        atEnd.ShouldBeLessThan(later);
        heard.ShouldAllBe(s => float.IsFinite(s) && Math.Abs(s) <= 1f);
    }

    [Fact]
    public void A_held_trigger_strikes_once()
    {
        var held = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort), Pulse(Rate, Rate));
        var tapped = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort), Pulse(1, Rate));

        held.ShouldBe(tapped);
    }

    [Fact]
    public void Struck_again_it_starts_again()
    {
        var trigger = new float[Rate];
        trigger[0] = 1f;
        trigger[Rate / 2] = 1f;

        var heard = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Decay, 0.3f)), trigger);

        Rms(heard.AsSpan(Rate / 2 - Rate / 40, Rate / 40)).ShouldBeLessThan(Rms(heard.AsSpan(Rate / 2, Rate / 40)));
    }

    /// <summary>
    /// The clock in a plane wraps at sixteen seconds. A ring is over by then, and a
    /// wrapped age is silence rather than a second strike.
    /// </summary>
    [Fact]
    public void Sixteen_seconds_after_a_strike_it_is_not_struck_again()
    {
        var program = Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Decay, 8f)).CompileForAudio(Catalog).Program;
        var state = new DelayState(program, 1000);
        var registers = program.AllocateRegisters();
        var loud = new List<(double At, double Level)>();

        // A thousand evaluations a second are enough to keep time; the ring itself is not the point.
        for (var i = 0; i < 18_000; i++)
        {
            program.Evaluate(i == 0 ? 1d : 0d, 0d, i / 1000d, registers, default, state);
            if (i % 500 == 0) loud.Add((i / 1000d, Math.Abs(registers[program.OutputBase])));
        }

        loud.Where(l => l.At >= 15.5).ShouldAllBe(l => l.Level == 0d);
    }

    [Fact]
    public void Struck_on_its_edge_nothing_moves()
    {
        var heard = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (StrikeX, 0f)), Pulse(1, Rate / 10));

        heard.ShouldAllBe(s => s == 0f);
    }

    [Fact]
    public void Sand_covers_a_plate_at_rest_and_is_thrown_off_where_a_strike_moves_it()
    {
        var resting = new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, triggerPort: null));

        resting.Planes.ShouldBe(3);
        resting.Frame(0.3, 0.2).ShouldBe(1d);

        // Struck (x is the trigger, so a pixel at x = 1 strikes as it is drawn), a point
        // off the nodal lines loses its sand and the plate's edge (y = 1) keeps it.
        new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, Trigger)).Frame(1d, 0.2).ShouldBeLessThan(0.9);
        new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, Trigger)).Frame(1d, 1d).ShouldBe(1d, 1e-6);
    }

    [Fact]
    public void Nothing_moves_until_a_strike_and_the_edge_never_does()
    {
        new Pixel(Wired(Plate, Motion, NodeCatalog.OutputColorPort, triggerPort: null)).Frame(0.3, 0.2).ShouldBe(0d);
        new Pixel(Wired(Plate, Motion, NodeCatalog.OutputColorPort, Trigger)).Frame(1d, 0.2).ShouldBeGreaterThan(0.01);
        new Pixel(Wired(Plate, Motion, NodeCatalog.OutputColorPort, Trigger)).Frame(1d, 1d).ShouldBe(0d, 1e-6);
    }

    /// <summary>
    /// A strike throws the sand off the middle of the plate, and it lies there again
    /// once the ring has died down. The strike arrives on Coordinates' aspect so the
    /// pixel can stay put.
    /// </summary>
    [Fact]
    public void The_ring_dies_away_and_the_sand_comes_back()
    {
        (int, float)[] knobs = [(Decay, 1f)];
        var moving = new Pixel(Wired(Plate, Motion, NodeCatalog.OutputColorPort, Trigger, NodeCatalog.CoordAspectPort, knobs));
        var sand = new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, Trigger, NodeCatalog.CoordAspectPort, knobs));

        var atStrike = (Motion: moving.Frame(0d, 0d, aspect: 1d), Sand: sand.Frame(0d, 0d, aspect: 1d));
        moving.Skip(180);
        sand.Skip(180);
        var later = (Motion: moving.Frame(0d, 0d, aspect: 1d), Sand: sand.Frame(0d, 0d, aspect: 1d));

        later.Motion.ShouldBeLessThan(atStrike.Motion);
        later.Motion.ShouldBeGreaterThan(0d);
        atStrike.Sand.ShouldBeLessThan(0.1);
        later.Sand.ShouldBeGreaterThan(0.5);
    }

    [Fact]
    public void A_harder_strike_rings_louder_and_throws_more_sand()
    {
        var soft = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (1, 0.25f)), Pulse(1, Rate / 10));
        var hard = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (1, 1f)), Pulse(1, Rate / 10));

        Rms(hard).ShouldBe(Rms(soft) * 4, Rms(soft) * 0.01);

        // Down a column of the plate, the hard strike leaves less sand in all.
        var softSand = new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, Trigger, knobs: (1, 0.25f)));
        var hardSand = new Pixel(Wired(Plate, Figure, NodeCatalog.OutputColorPort, Trigger, knobs: (1, 1f)));
        var column = Enumerable.Range(0, 21).Select(i => i / 10d - 1d).ToArray();

        column.Sum(y => hardSand.Frame(1d, y)).ShouldBeLessThan(column.Sum(y => softSand.Frame(1d, y)));
    }

    [Fact]
    public void Its_pitch_follows_freq()
    {
        var low = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Freq, 110f), (Aspect, 1.3f)), Pulse(1, Rate / 4));
        var high = Heard(Wired(Plate, Out, NodeCatalog.OutputLeftPort, 0, NodeCatalog.CoordXPort, (Freq, 220f), (Aspect, 1.3f)), Pulse(1, Rate / 4));

        ((double)Crossings(high)).ShouldBeGreaterThan(Crossings(low) * 1.5);
    }

    [Fact]
    public void The_figure_survives_to_the_shader()
    {
        var program = Wired(Plate, Figure, NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;

        program.PlaneCount.ShouldBe(3);
        LowersToEveryDialect(program);
    }

    [Fact]
    public void The_strike_is_kept_in_three_planes_and_no_cell()
    {
        var program = Wired(Plate, Out, NodeCatalog.OutputLeftPort).CompileForAudio(Catalog).Program;

        program.PlaneCount.ShouldBe(3);
        program.UnitCount.ShouldBe(0);
    }

    /// <summary>Overtones reads the plate at eight places, and it is one plate struck once, not eight.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_at_every_partial_it_rings_once(bool heard)
    {
        var b = new PatchBuilder(Catalog);
        var plate = b.Add(Plate);
        var overtones = b.Add(Overtones);
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(plate, Motion, overtones, 0)
         .Wire(overtones, heard ? 0 : 1, output, heard ? NodeCatalog.OutputLeftPort : NodeCatalog.OutputColorPort);

        var program = heard ? b.Patch.CompileForAudio(Catalog).Program : b.Patch.CompileForVideo(Catalog).Program;

        program.PlaneCount.ShouldBe(3);
        program.Ops.Count(op => op.Code == OpCode.Exp).ShouldBe(9 + 7, "an envelope per mode, and the tilt of each partial above the first");
    }

    /// <summary>
    /// Sand as the editor plays it, its knobs live: each plate and the harmonograph
    /// keep their planes once, however many partials read the plate Overtones hears.
    /// The kick is a third plate, heard and not seen.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sand_played_strikes_each_plate_once(bool heard)
    {
        var sand = ShippedPlugins.Loaded.Presets.Single(p => p.Name == "Sand").Build(Catalog);

        var program = heard
            ? sand.CompileForAudio(Catalog, played: true).Program
            : sand.CompileForVideo(Catalog, played: true).Program;

        program.PlaneCount.ShouldBe(heard ? 3 + 3 + 3 + 5 : 3 + 3 + 5);
    }

    private static int Crossings(float[] samples)
    {
        var count = 0;
        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] < 0f != samples[i] < 0f) count++;
        return count;
    }
}
