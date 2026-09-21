using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class MaximizerTests
{
    private const string Type = "flyback.mastering.maximizer";

    private const int Amount = 2;
    private const int Style = 3;

    /// <summary>The limiter's lookahead, 1.5 ms, in samples.</summary>
    private const int Late = Rate * 3 / 2000;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Maximizer");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    /// <summary>
    /// At nought a quiet tone comes through at its own level, a lookahead late:
    /// the bands add back up and nothing is compressed.
    /// </summary>
    [Fact]
    public void At_nought_a_quiet_signal_comes_through_at_its_own_level()
    {
        var signal = Sine(440d, 0.2f, 0.5);

        Decibels(Settled(Play(Type, signal, knobs: [(Amount, 0f)]).Left) / 0.2).ShouldBe(0d, 0.05d);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    [InlineData(4f)]
    public void Nothing_comes_out_over_the_ceiling(float style)
    {
        var music = Mix(Sine(60d, 0.6f, 1), Sine(700d, 0.4f, 1), Sine(5000d, 0.2f, 1));
        var ceiling = MathF.Pow(10f, -1f / 20f);

        foreach (var amount in new[] { 0f, 0.5f, 1f })
            Play(Type, music, knobs: [(Amount, amount), (Style, style)])
                .Left.ShouldAllBe(s => MathF.Abs(s) <= ceiling + 1e-5f);
    }

    /// <summary>A quiet mix, so what is measured is the compression and not the ceiling.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    [InlineData(4f)]
    public void More_amount_is_louder(float style)
    {
        var music = Mix(Sine(60d, 0.06f, 1), Sine(700d, 0.04f, 1), Sine(5000d, 0.02f, 1));

        var levels = new[] { 0f, 0.5f, 1f }
            .Select(amount => Rms(Play(Type, music, knobs: [(Amount, amount), (Style, style)]).Left))
            .ToArray();

        levels[1].ShouldBeGreaterThan(levels[0]);
        levels[2].ShouldBeGreaterThan(levels[1]);
    }

    /// <summary>A loud passage and a quiet one come out nearer each other.</summary>
    [Fact]
    public void It_narrows_the_gap_between_loud_and_quiet()
    {
        var loud = Sine(700d, 0.5f, 0.5);
        var quiet = Sine(700d, 0.05f, 0.5);

        var inGap = Decibels(0.5 / 0.05);
        var outGap = Decibels(
            Settled(Play(Type, loud, knobs: [(Amount, 1f)]).Left)
            / Settled(Play(Type, quiet, knobs: [(Amount, 1f)]).Left));

        outGap.ShouldBeLessThan(inGap - 6d);
    }

    [Fact]
    public void The_bright_style_lifts_the_top_more_than_glue_does()
    {
        var top = Sine(8000d, 0.05f, 0.5);

        var glue = Settled(Play(Type, top, knobs: [(Amount, 1f), (Style, 1f)]).Left);
        var bright = Settled(Play(Type, top, knobs: [(Amount, 1f), (Style, 3f)]).Left);

        Decibels(bright / glue).ShouldBe(3d, 0.5d);
    }

    [Fact]
    public void An_unpatched_right_carries_the_left()
    {
        var (left, right) = Play(Type, Sine(300d, 0.4f, 0.2), knobs: [(Amount, 0.7f)]);

        right.ShouldBe(left);
    }

    [Fact]
    public void The_sound_is_a_lookahead_late()
    {
        var click = Hold(0f, 0.05).Select((_, i) => i == 0 ? 0.2f : 0f).ToArray();
        var heard = Play(Type, click, knobs: [(Amount, 0f)]).Left;

        // The first evaluation is the one every module passes straight through,
        // before its memory says it has one.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        heard.Skip(1).Take(Late - 1).ShouldAllBe(s => s == 0f);
        heard[Late].ShouldNotBe(0f);
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        Picture(Type, values, (Amount, 1f), (Style, 4f)).ShouldBe(values);
    }

    private static float[] Mix(params float[][] parts) =>
        [.. Enumerable.Range(0, parts[0].Length).Select(i => parts.Sum(p => p[i]))];

    private static double Rms(float[] signal) =>
        Math.Sqrt(signal.Skip(signal.Length / 2).Average(s => (double)s * s));
}
