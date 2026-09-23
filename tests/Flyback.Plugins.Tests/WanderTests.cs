using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Wander, read at points in time. It is stateless, so no renderer is involved.
/// </summary>
public class WanderTests
{
    private const string WanderType = "flyback.voice.wander";

    private const int RatePort = 1;
    private const int SeedPort = 2;
    private const int LowPort = 3;
    private const int HighPort = 4;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_beside_the_oscillators()
    {
        var def = Catalog.Get(WanderType).ShouldNotBeNull();

        def.Name.ShouldBe("Wander");
        def.Category.ShouldBe(ModuleCategories.Oscillators);
        def.Sinks.ShouldBe(ModuleSinks.Both);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Inputs[0].Domain.ShouldBeTrue();
        Catalog.ProviderOf(WanderType)!.Id.ShouldBe("flyback.voice");
        Catalog.All.Count(d => d.TypeId.Split('.')[^1] == "wander").ShouldBe(1);
    }

    [Fact]
    public void It_stays_between_low_and_high_and_uses_most_of_the_way()
    {
        var wander = Reader((RatePort, 1f), (LowPort, 200f), (HighPort, 1200f));
        var walk = Enumerable.Range(0, 4_000).Select(i => wander(i * 0.05)).ToList();

        walk.Min().ShouldBeGreaterThanOrEqualTo(200f);
        walk.Max().ShouldBeLessThanOrEqualTo(1200f);
        (walk.Max() - walk.Min()).ShouldBeGreaterThan(700f);
    }

    [Fact]
    public void It_moves_smoothly_and_at_its_rate()
    {
        var slow = Reader((RatePort, 0.5f));
        var fast = Reader((RatePort, 8f));

        Travel(slow).ShouldBeLessThan(Travel(fast) / 4f);

        // No step anywhere: a millisecond is never more than a sliver of the range.
        for (var i = 0; i < 2_000; i++)
            Math.Abs(fast(i * 0.001 + 0.001) - fast(i * 0.001)).ShouldBeLessThan(0.05f);

        static float Travel(Func<double, float> wander) =>
            Enumerable.Range(0, 2_000).Sum(i => Math.Abs(wander((i + 1) * 0.01) - wander(i * 0.01)));
    }

    [Fact]
    public void Whole_seeds_share_nothing_and_the_same_seed_moves_together()
    {
        var one = Reader((RatePort, 2f), (SeedPort, 1f));
        var same = Reader((RatePort, 2f), (SeedPort, 1f));
        var other = Reader((RatePort, 2f), (SeedPort, 2f));

        var times = Enumerable.Range(0, 400).Select(i => i * 0.37).ToList();

        times.Select(one).ShouldBe(times.Select(same));
        times.Count(t => Math.Abs(one(t) - other(t)) > 0.05f).ShouldBeGreaterThan(300);
    }

    [Fact]
    public void The_picture_follows_what_the_speakers_follow_at_every_pixel()
    {
        var audio = Reader((RatePort, 1.3f), (SeedPort, 3f));

        foreach (var (x, y) in new[] { (0f, 0f), (-1.2f, 0.7f), (0.4f, -0.9f) })
        {
            var video = Reader(true, x, y, (RatePort, 1.3f), (SeedPort, 3f));

            foreach (var t in new[] { 0.0, 0.1, 0.33, 2.9, 71.03 })
                video(t).ShouldBe(audio(t), 1e-6f);
        }
    }

    /// <summary>
    /// Two Values, a Multiply, a Clouds and a Remap, against this. Equal rather than
    /// close, so a preset moved onto it has the weather it had.
    /// </summary>
    [Fact]
    public void It_is_the_five_modules_it_stands_for_to_the_last_bit()
    {
        var b = new PatchBuilder(Catalog);
        var clock = b.Add("time");
        var lane = b.Add("value", (0, 2.31f));
        var drift = b.Add("math.mul", (1, 0.06f));
        var mood = b.Add("pattern.clouds", (3, 1f));
        var hang = b.Add("math.remap", (1, 0f), (2, 1f), (3, 0.24f), (4, 0.6f));
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(clock, 0, drift, 0).Wire(lane, 0, mood, 0).Wire(lane, 0, mood, 1).Wire(drift, 0, mood, 2)
         .Wire(mood, 0, hang, 0).Wire(hang, 0, sink, NodeCatalog.OutputLeftPort);

        var byHand = Exact(b.Patch, false, 0f, 0f);
        var module = Exact(
            Wired(false, (RatePort, 0.06f), (SeedPort, 2.31f), (LowPort, 0.24f), (HighPort, 0.6f)), false, 0f, 0f);

        foreach (var t in new[] { 0.0, 0.013, 7.7, 123.456, 3010.07 })
            module(t).ShouldBe(byHand(t));
    }

    // --- harness -----------------------------------------------------------------

    private static Func<double, float> Reader(params (int Port, float Value)[] knobs) =>
        Reader(false, 0f, 0f, knobs);

    private static Func<double, float> Reader(bool video, float x, float y, params (int Port, float Value)[] knobs)
    {
        var exact = Exact(Wired(video, knobs), video, x, y);
        return t => (float)exact(t);
    }

    private static Patch Wired(bool video, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var wander = b.Add(WanderType, knobs);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(wander, 0, sink, video ? NodeCatalog.OutputColorPort : NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static Func<double, double> Exact(Patch patch, bool video, float x, float y)
    {
        var program = video ? patch.CompileForVideo(Catalog).Program : patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        return t =>
        {
            program.Evaluate(x, y, t, registers, default);
            return registers[program.OutputBase];
        };
    }
}
