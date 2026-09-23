using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Figures;

namespace Flyback.Plugins.Tests;

/// <summary>A picture is read along a row as the overtones of a tone, and drawn back.</summary>
public class OvertonesTests
{
    private const int Spectrum = 0, Row = 3;
    private const int Out = 0, Bars = 1, Wave = 2;

    [Fact]
    public void Figures_offers_it_for_both_sinks_with_its_own_face()
    {
        var def = Catalog.Get(Overtones).ShouldNotBeNull();

        def.Name.ShouldBe("Overtones");
        def.Category.ShouldBe("Figures");
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Skin.ShouldBeOfType<ModuleSkin.Artwork>();
        def.Inputs[Spectrum].Swept.ShouldBeTrue();
        def.Outputs.Select(o => o.Name).ShouldBe(["out", "bars", "wave"]);
    }

    [Fact]
    public void A_dark_picture_is_a_silent_tone()
    {
        Heard(Wired(Overtones, Out, NodeCatalog.OutputLeftPort, triggerPort: null), new float[Rate / 20]).ShouldAllBe(s => s == 0f);
    }

    /// <summary>Coordinates' y into 'spectrum': y is one at the top of the screen, so only the top row hears anything.</summary>
    [Fact]
    public void The_picture_is_read_along_the_row()
    {
        var top = Heard(ReadingY((Row, 0f)), new float[Rate / 10]);
        var bottom = Heard(ReadingY((Row, 1f)), new float[Rate / 10]);

        bottom.ShouldAllBe(s => s == 0f);
        Rms(top).ShouldBeGreaterThan(0.1);
        top.ShouldAllBe(s => Math.Abs(s) <= 1f);
    }

    /// <summary>Each partial reads its own place across the row, so a picture feeding it is lowered once per partial.</summary>
    [Fact]
    public void Each_partial_is_another_copy_of_what_feeds_it()
    {
        var b = new PatchBuilder(Catalog);
        var clouds = b.Add(NodeCatalog.CloudsTypeId);
        var overtones = b.Add(Overtones);
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(clouds, 0, overtones, Spectrum).Wire(overtones, Out, output, NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio(Catalog).Program;

        program.Ops.Count(op => op.Code == OpCode.Noise3).ShouldBe(8);
        program.PhaseCount.ShouldBe(1, "one phase, turned up the partials");
    }

    /// <summary>A partial is a place along the row, so what reads no place is lowered once for all of them.</summary>
    [Fact]
    public void What_reads_no_place_is_lowered_once_however_many_partials_read_it()
    {
        var b = new PatchBuilder(Catalog);
        var sine = b.Add(NodeCatalog.SineTypeId, (1, 0.5f));
        var overtones = b.Add(Overtones);
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(sine, 0, overtones, Spectrum).Wire(overtones, Out, output, NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio(Catalog).Program;

        program.PhaseCount.ShouldBe(1 + 1, "the partials' one phase, and the sine's");
    }

    /// <summary>
    /// Three tenths of Coordinates' x into 'spectrum': the left of the row is dark and the
    /// right reads half, so the last bar stands half way up the screen from the floor.
    /// </summary>
    [Fact]
    public void The_bars_stand_on_the_floor_as_tall_as_the_readings()
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var dimmed = b.Add("math.mul", (1, 0.3f));
        var overtones = b.Add(Overtones);
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, NodeCatalog.CoordXPort, dimmed, 0)
         .Wire(dimmed, 0, overtones, Spectrum)
         .Wire(overtones, Bars, output, NodeCatalog.OutputColorPort);

        var pixel = new Pixel(b.Patch);
        var lastBin = 16d / 9d * (1d - 1d / 16d);

        pixel.Frame(-lastBin, -0.9).ShouldBe(0d);
        pixel.Frame(lastBin, -0.9).ShouldBe(1d);
        pixel.Frame(lastBin, 0.9).ShouldBe(0d);
    }

    [Theory]
    [InlineData(NodeCatalog.OutputLeftPort, Out)]
    [InlineData(NodeCatalog.OutputColorPort, Bars)]
    [InlineData(NodeCatalog.OutputColorPort, Wave)]
    public void Every_output_survives_to_the_shader(int into, int from)
    {
        var program = ReadingY((Row, 0.5f), from, into).CompileForVideo(Catalog).Program;

        program.PlaneCount.ShouldBe(0);
        LowersToEveryDialect(program);
    }

    private static Patch ReadingY((int Port, float Value) knob, int from = Out, int into = NodeCatalog.OutputLeftPort)
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var overtones = b.Add(Overtones, knob);
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(coord, NodeCatalog.CoordYPort, overtones, Spectrum).Wire(overtones, from, output, into);

        return b.Patch;
    }
}
