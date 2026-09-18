using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class EqTests
{
    private const string Type = "flyback.mastering.eq";

    private const int LowCut = 2;
    private const int LowFreq = 3;
    private const int LowGain = 4;
    private const int MidFreq = 5;
    private const int MidGain = 6;
    private const int MidQ = 7;
    private const int HighFreq = 8;
    private const int HighGain = 9;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("EQ");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    [Fact]
    public void At_its_defaults_it_is_exactly_a_wire()
    {
        var left = Sine(440d, 0.7f, 0.1);
        var right = Sine(3000d, 0.4f, 0.1);

        var heard = Play(Type, left, right);

        heard.Left.ShouldBe(left);
        heard.Right.ShouldBe(right);
    }

    /// <summary>A bell's gain at its own centre is the gain asked for, whatever its width.</summary>
    [Theory]
    [InlineData(6f, 0.7f)]
    [InlineData(-9f, 2f)]
    [InlineData(12f, 0.3f)]
    public void A_bell_has_its_gain_at_its_centre(float gain, float q)
    {
        var heard = Play(Type, Sine(1000d, 0.25f, 0.5), knobs: [(MidFreq, 1000f), (MidGain, gain), (MidQ, q)]);

        Decibels(Settled(heard.Left) / 0.25).ShouldBe(gain, 0.1d);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(-6f)]
    public void The_low_shelf_moves_everything_well_under_it(float gain)
    {
        var heard = Play(Type, Sine(20d, 0.25f, 1), knobs: [(LowFreq, 400f), (LowGain, gain)]);
        var above = Play(Type, Sine(8000d, 0.25f, 0.2), knobs: [(LowFreq, 400f), (LowGain, gain)]);

        Decibels(Settled(heard.Left) / 0.25).ShouldBe(gain, 0.1d);
        Decibels(Settled(above.Left) / 0.25).ShouldBe(0d, 0.1d);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(-6f)]
    public void The_high_shelf_moves_everything_well_over_it(float gain)
    {
        var heard = Play(Type, Sine(18000d, 0.25f, 0.2), knobs: [(HighFreq, 2000f), (HighGain, gain)]);
        var under = Play(Type, Sine(50d, 0.25f, 1), knobs: [(HighFreq, 2000f), (HighGain, gain)]);

        Decibels(Settled(heard.Left) / 0.25).ShouldBe(gain, 0.15d);
        Decibels(Settled(under.Left) / 0.25).ShouldBe(0d, 0.1d);
    }

    [Fact]
    public void The_low_cut_takes_away_what_is_under_it()
    {
        var rumble = Play(Type, Sine(10d, 0.5f, 2), knobs: [(LowCut, 100f)]);
        var voice = Play(Type, Sine(1000d, 0.5f, 0.2), knobs: [(LowCut, 100f)]);

        Decibels(Settled(rumble.Left) / 0.5).ShouldBeLessThan(-35d);
        Decibels(Settled(voice.Left) / 0.5).ShouldBe(0d, 0.05d);
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        Picture(Type, values, (LowCut, 100f), (LowGain, 12f), (HighGain, -12f)).ShouldBe(values);
    }
}
