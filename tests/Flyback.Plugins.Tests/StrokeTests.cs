using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Stroke envelope, read at points in time. It is stateless, so no renderer is involved.
/// </summary>
public class StrokeTests
{
    private const string StrokeType = "flyback.voice.stroke";

    private const int Out = 0;
    private const int Phase = 1;

    private const int RatePort = 1;
    private const int OffsetPort = 2;
    private const int CurvePort = 3;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_timing()
    {
        var def = Catalog.Get(StrokeType).ShouldNotBeNull();

        def.Name.ShouldBe("Stroke");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Outputs.Select(p => p.Name).ShouldBe(["out", "phase"]);
        def.Inputs[0].Domain.ShouldBeTrue();
        Catalog.ProviderOf(StrokeType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "stroke").ShouldBe(1);
    }

    [Fact]
    public void It_starts_every_stroke_at_full_and_has_fallen_by_the_end_of_it()
    {
        var stroke = Reader(Out, (RatePort, 4f), (CurvePort, 1f));

        stroke(0.0).ShouldBe(1f, 1e-6f);
        stroke(0.125).ShouldBe(0.5f, 1e-6f);
        stroke(0.2499).ShouldBeLessThan(0.001f);
        stroke(0.25).ShouldBe(1f, 1e-6f);
    }

    [Fact]
    public void Curve_bends_the_fall_without_moving_its_ends()
    {
        var straight = Reader(Out, (CurvePort, 1f));
        var plucked = Reader(Out, (CurvePort, 3f));

        plucked(0.0).ShouldBe(1f, 1e-6f);
        plucked(0.5).ShouldBe(0.125f, 1e-6f);
        plucked(0.5).ShouldBeLessThan(straight(0.5));
    }

    [Fact]
    public void Half_the_rate_and_half_an_offset_is_the_backbeat()
    {
        var backbeat = Reader(Out, (RatePort, 0.5f), (OffsetPort, 0.5f), (CurvePort, 1f));

        backbeat(1.0).ShouldBe(1f, 1e-6f);
        backbeat(3.0).ShouldBe(1f, 1e-6f);
        backbeat(2.0).ShouldBe(0.5f, 1e-6f);
    }

    [Fact]
    public void Phase_is_how_far_through_the_stroke_it_is()
    {
        var phase = Reader(Phase, (RatePort, 2f));

        phase(0.0).ShouldBe(0f, 1e-6f);
        phase(0.25).ShouldBe(0.5f, 1e-6f);
        phase(1.375).ShouldBe(0.75f, 1e-6f);
    }

    [Fact]
    public void The_picture_sees_the_same_stroke_as_the_speakers()
    {
        foreach (var port in new[] { Out, Phase })
        {
            var audio = Reader(port, (RatePort, 3f), (CurvePort, 5f));
            var video = Reader(port, video: true, (RatePort, 3f), (CurvePort, 5f));

            foreach (var t in new[] { 0.0, 0.1, 0.33, 2.9, 71.03 })
                video(t).ShouldBe(audio(t), 1e-6f);
        }
    }

    /// <summary>
    /// A Multiply, an Add, a Fraction, a Subtract and a Power, against this. Equal
    /// rather than close, so a preset moved onto it plays the same samples.
    /// </summary>
    [Fact]
    public void It_is_the_five_modules_it_stands_for_to_the_last_bit()
    {
        var b = new PatchBuilder(Catalog);
        var clock = b.Add("time");
        var rated = b.Add("math.mul", (1, 4f));
        var offset = b.Add("math.add", (1, 0.5f));
        var gone = b.Add("math.fract");
        var left = b.Add("math.sub", (0, 1f));
        var bent = b.Add("math.pow", (1, 7f));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(clock, 0, rated, 0).Wire(rated, 0, offset, 0).Wire(offset, 0, gone, 0)
         .Wire(gone, 0, left, 1).Wire(left, 0, bent, 0).Wire(bent, 0, sink, NodeCatalog.OutputLeftPort);

        var byHand = Exact(b.Patch);
        var module = Exact(Wired(Out, false, (RatePort, 4f), (OffsetPort, 0.5f), (CurvePort, 7f)));

        foreach (var t in new[] { 0.0, 0.013, 0.77, 12.3456, 301.07 })
            module(t).ShouldBe(byHand(t));
    }

    // --- harness -----------------------------------------------------------------

    private static Func<double, float> Reader(int port, params (int Port, float Value)[] knobs) =>
        Reader(port, false, knobs);

    private static Func<double, float> Reader(int port, bool video, params (int Port, float Value)[] knobs)
    {
        var exact = Exact(Wired(port, video, knobs), video);
        return t => (float)exact(t);
    }

    private static Patch Wired(int port, bool video, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var stroke = b.Add(StrokeType, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(stroke, port, sink, video ? NodeCatalog.OutputColorPort : NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static Func<double, double> Exact(Patch patch, bool video = false)
    {
        var program = video ? patch.CompileForVideo(Catalog).Program : patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        return t =>
        {
            program.Evaluate(0f, 0f, t, registers, default);
            return registers[program.OutputBase];
        };
    }
}
