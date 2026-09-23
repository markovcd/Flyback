using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Euclid rhythm, read at points in time. It is stateless, so no renderer is involved.
/// </summary>
public class EuclidTests
{
    private const string EuclidType = "flyback.voice.euclid";

    private const int Gate = 0;
    private const int Hit = 1;
    private const int Index = 2;
    private const int Stroke = 3;

    private const int RatePort = 1;
    private const int StepsPort = 2;
    private const int HitsPort = 3;
    private const int RotatePort = 4;
    private const int LengthPort = 5;
    private const int CurvePort = 6;

    private const float Rate = 4f;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_under_timing()
    {
        var def = Catalog.Get(EuclidType).ShouldNotBeNull();

        def.Name.ShouldBe("Euclid");
        def.Category.ShouldBe(ModuleCategories.Timing);
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Outputs.Select(p => p.Name).ShouldBe(["gate", "hit", "index", "stroke"]);
        Catalog.ProviderOf(EuclidType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "euclid").ShouldBe(1);
    }

    [Fact]
    public void Three_in_eight_is_the_tresillo()
    {
        Pattern(8, 3).ShouldBe("x..x..x.");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 5)]
    [InlineData(12, 7)]
    [InlineData(16, 9)]
    [InlineData(13, 5)]
    public void The_hits_are_all_there_and_spread_as_evenly_as_they_go(int steps, int hits)
    {
        var pattern = Pattern(steps, hits);

        pattern.Count(c => c == 'x').ShouldBe(hits);
        pattern[0].ShouldBe('x');

        var at = Enumerable.Range(0, steps).Where(i => pattern[i] == 'x').ToList();
        var gaps = at.Zip(at.Skip(1).Append(at[0] + steps), (a, b) => b - a).ToList();
        (gaps.Max() - gaps.Min()).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Rotate_slides_the_pattern_along()
    {
        var plain = Pattern(8, 3);
        var rotated = Pattern(8, 3, rotate: 2);

        rotated.ShouldBe(plain[2..] + plain[..2]);
    }

    [Fact]
    public void Out_of_range_counts_are_held_to_something_that_plays()
    {
        Pattern(8, 20).ShouldBe("xxxxxxxx");
        Pattern(8, 0).ShouldBe("........");
        Pattern(0, 3).ShouldBe("x");
    }

    [Fact]
    public void The_gate_opens_on_hits_for_its_length()
    {
        var gate = Reader(Gate, (StepsPort, 8f), (HitsPort, 3f), (LengthPort, 0.5f));

        gate(0.25 / Rate).ShouldBe(1f, 1e-6f);
        gate(0.75 / Rate).ShouldBe(0f, 1e-6f);
        gate(1.25 / Rate).ShouldBe(0f, 1e-6f);
        gate(3.25 / Rate).ShouldBe(1f, 1e-6f);
    }

    [Fact]
    public void Index_says_how_far_through_the_loop_it_is()
    {
        var index = Reader(Index, (StepsPort, 8f), (HitsPort, 3f));

        index(0.5 / Rate).ShouldBe(0f, 1e-6f);
        index(6.5 / Rate).ShouldBe(0.75f, 1e-6f);
        index(9.5 / Rate).ShouldBe(0.125f, 1e-6f);
    }

    [Fact]
    public void The_stroke_falls_over_a_hit_step_and_is_nothing_on_the_others()
    {
        // Three in eight is x..x..x. — the second step is a rest and the fourth a hit.
        var stroke = Reader(Stroke, (StepsPort, 8f), (HitsPort, 3f), (CurvePort, 1f));

        stroke(3.0 / Rate).ShouldBe(1f, 1e-5f);
        stroke(3.5 / Rate).ShouldBe(0.5f, 1e-5f);
        stroke(1.25 / Rate).ShouldBe(0f);
    }

    /// <summary>
    /// A Stroke at the same rate and a Multiply by 'hit', against the 'stroke'
    /// output. Equal rather than close, so a drum moved onto it is the drum it was.
    /// </summary>
    [Fact]
    public void The_stroke_is_a_stroke_let_through_on_the_hits_to_the_last_bit()
    {
        var b = new PatchBuilder(Catalog);
        var euclid = b.Add(EuclidType, (RatePort, Rate), (StepsPort, 16f), (HitsPort, 5f), (RotatePort, 7f));
        var envelope = b.Add("flyback.voice.stroke", (1, Rate), (3, 7f));
        var let = b.Add("math.mul");
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(envelope, 0, let, 0).Wire(euclid, Hit, let, 1).Wire(let, 0, sink, NodeCatalog.OutputLeftPort);

        var byHand = b.Patch.CompileForAudio(Catalog).Program;
        var registers = byHand.AllocateRegisters();
        var stroke = Reader(Stroke, (StepsPort, 16f), (HitsPort, 5f), (RotatePort, 7f), (CurvePort, 7f));

        for (var i = 0; i < 4_000; i++)
        {
            var t = i / 997d;

            byHand.Evaluate(0f, 0f, t, registers, default);
            stroke(t).ShouldBe((float)registers[byHand.OutputBase]);
        }
    }

    [Fact]
    public void The_picture_sees_the_same_rhythm_as_the_speakers()
    {
        foreach (var port in new[] { Gate, Hit, Index, Stroke })
        {
            var audio = Reader(port, (StepsPort, 12f), (HitsPort, 5f));
            var video = Reader(port, video: true, (StepsPort, 12f), (HitsPort, 5f));

            foreach (var t in new[] { 0.0, 0.1, 0.33, 2.9, 71.03 })
                video(t).ShouldBe(audio(t), 1e-6f);
        }
    }

    // --- harness -----------------------------------------------------------------

    private static string Pattern(int steps, int hits, int rotate = 0)
    {
        var hit = Reader(Hit, (StepsPort, steps), (HitsPort, hits), (RotatePort, rotate));
        var length = Math.Max(steps, 1);

        return string.Concat(Enumerable.Range(0, length).Select(i => hit((i + 0.5) / Rate) > 0.5f ? 'x' : '.'));
    }

    private static Func<double, float> Reader(int port, params (int Port, float Value)[] knobs) =>
        Reader(port, false, knobs);

    private static Func<double, float> Reader(int port, bool video, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var euclid = NodeInstance.Create(Catalog.Require(EuclidType), 0, 0);
        euclid.InputValues[RatePort] = Rate;
        foreach (var (at, value) in knobs) euclid.InputValues[at] = value;

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        sink.InputValues[NodeCatalog.OutputVolumePort] = 1f;

        patch.Nodes.Add(euclid);
        patch.Nodes.Add(sink);
        patch.Connect(euclid.Id, port, sink.Id, video ? NodeCatalog.OutputColorPort : NodeCatalog.OutputLeftPort);

        var program = video ? patch.CompileForVideo(Catalog).Program : patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        return t =>
        {
            program.Evaluate(0f, 0f, t, registers, default);
            return (float)registers[program.OutputBase];
        };
    }
}
