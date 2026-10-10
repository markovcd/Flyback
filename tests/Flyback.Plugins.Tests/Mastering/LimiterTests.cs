using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class LimiterTests
{
    private const string Type = "flyback.mastering.limiter";

    private const int Ceiling = 2;
    private const int Release = 3;
    private const int Lookahead = 4;

    private const int Gain = 2;

    /// <summary>The default lookahead, 1.5 ms, in samples.</summary>
    private const int Late = Rate * 3 / 2000;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Limiter");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    [Fact]
    public void Under_the_ceiling_it_is_the_input_a_lookahead_later()
    {
        var signal = Sine(440d, 0.5f, 0.1);
        var (left, right) = Play(Type, signal);

        for (var i = Late; i < signal.Length; i++)
        {
            left[i].ShouldBe(signal[i - Late], 1e-5f);
            right[i].ShouldBe(left[i], "an unpatched right carries the left");
        }
    }

    /// <summary>
    /// Twelve decibels over a -1 dB ceiling, arriving out of silence. Checked as
    /// the gain times the delayed input, so the clamp at the end cannot be what
    /// passes it.
    /// </summary>
    [Theory]
    [InlineData(1.5f)]
    [InlineData(0.5f)]
    [InlineData(5f)]
    public void Nothing_comes_out_over_the_ceiling(float lookahead)
    {
        var late = (int)Math.Round(lookahead * Rate / 1000f);
        var burst = Hold(0f, 0.05).Concat(Sine(997d, 3.55f, 0.2)).Concat(Sine(60d, 0.5f, 0.1)).ToArray();

        var (left, gain) = Play(Type, burst, heard: (0, Gain), knobs: [(Lookahead, lookahead)]);

        var ceiling = MathF.Pow(10f, -1f / 20f);

        for (var i = late; i < burst.Length; i++)
        {
            MathF.Abs(gain[i] * burst[i - late]).ShouldBeLessThanOrEqualTo(ceiling + 1e-5f, $"sample {i}");
            MathF.Abs(left[i]).ShouldBeLessThanOrEqualTo(ceiling + 1e-6f);
        }

        Decibels(Settled(left.Skip(Rate / 20).Take(Rate / 5).ToArray())).ShouldBe(-1d, 0.05d);
    }

    [Fact]
    public void It_lets_go_on_its_release()
    {
        var burst = Hold(1f, 0.05).Concat(Hold(0.1f, 0.5)).ToArray();

        var (_, fast) = Play(Type, burst, heard: (0, Gain), knobs: [(Ceiling, -6f), (Release, -2f)]);
        var (_, slow) = Play(Type, burst, heard: (0, Gain), knobs: [(Ceiling, -6f), (Release, 0f)]);

        var after = Rate / 20 + Rate / 20;
        fast[after].ShouldBe(1f, 0.01f);
        slow[after].ShouldBeLessThan(0.7f);
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        Picture(Type, values, (Ceiling, -12f)).ShouldBe(values);
    }
}
