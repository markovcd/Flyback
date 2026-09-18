using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class CrossoverTests
{
    private const string Type = "flyback.mastering.crossover";

    private const int Low = 0;
    private const int Mid = 1;
    private const int High = 2;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Crossover");
        def.Sinks.ShouldBe(ModuleSinks.Audio);
    }

    /// <summary>
    /// The three bands summed, which a Mixer would do, at the same level as the
    /// input at every frequency — corners included.
    /// </summary>
    [Theory]
    [InlineData(30d)]
    [InlineData(200d)]
    [InlineData(700d)]
    [InlineData(2000d)]
    [InlineData(9000d)]
    public void The_bands_add_back_up_to_the_input_level(double hertz)
    {
        var signal = Sine(hertz, 0.5f, hertz < 100d ? 1 : 0.3);

        var lowAndMid = Play(Type, signal, heard: (Low, Mid));
        var high = Play(Type, signal, heard: (High, High)).Left;

        var sum = lowAndMid.Left.Zip(lowAndMid.Right, high).Select(t => t.First + t.Second + t.Third).ToArray();

        Decibels(Settled(sum) / 0.5).ShouldBe(0d, 0.05d);
    }

    [Theory]
    [InlineData(40d, Low)]
    [InlineData(630d, Mid)]
    [InlineData(10000d, High)]
    public void Each_band_has_its_own_part_of_the_spectrum(double hertz, int band)
    {
        var signal = Sine(hertz, 0.5f, hertz < 100d ? 1 : 0.3);

        var own = Settled(Play(Type, signal, heard: (band, band)).Left);
        Decibels(own / 0.5).ShouldBe(0d, 0.5d);

        foreach (var other in new[] { Low, Mid, High }.Where(b => b != band))
            Decibels(Settled(Play(Type, signal, heard: (other, other)).Left) / 0.5).ShouldBeLessThan(-12d);
    }

    [Fact]
    public void On_the_picture_everything_comes_out_of_low()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        Picture(Type, values).ShouldBe(values);
    }
}
