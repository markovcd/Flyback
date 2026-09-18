using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class CompressorTests
{
    private const string Type = "flyback.mastering.compressor";

    private const int Key = 2;
    private const int Threshold = 3;
    private const int Ratio = 4;
    private const int Attack = 5;
    private const int Knee = 7;
    private const int Makeup = 8;

    private const int Gain = 2;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_audio()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Compressor");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        Catalog.ProviderOf(Type)!.Id.ShouldBe("flyback.mastering");
    }

    /// <summary>Half scale is 6 dB over a -12 dB threshold, and at 4:1 comes out 1.5 dB over.</summary>
    [Fact]
    public void A_steady_level_is_turned_down_by_what_the_ratio_says()
    {
        var (left, _) = Play(Type, Hold(0.5f, 0.5), knobs: [(Threshold, -12f), (Ratio, 4f), (Knee, 0f), (Attack, -3f)]);

        var over = Decibels(0.5) + 12d;
        var expected = Math.Pow(10d, (-12d + over / 4d) / 20d);

        left[^1].ShouldBe((float)expected, 1e-4f);
    }

    [Fact]
    public void Below_the_threshold_it_is_a_wire()
    {
        var signal = Sine(440d, 0.05f, 0.2);
        var (left, right) = Play(Type, signal, knobs: [(Threshold, -18f)]);

        left.ShouldBe(signal);
        right.ShouldBe(signal, "an unpatched right carries the left");
    }

    [Fact]
    public void At_one_to_one_it_is_a_wire_however_loud()
    {
        var signal = Sine(440d, 0.9f, 0.2);

        Play(Type, signal, knobs: [(Ratio, 1f), (Threshold, -60f)]).Left.ShouldBe(signal);
    }

    [Fact]
    public void Both_sides_are_turned_down_by_the_louder()
    {
        var (left, right) = Play(Type, Hold(0.05f, 0.5), Hold(0.9f, 0.5), knobs: [(Threshold, -20f), (Attack, -3f)]);

        (left[^1] / 0.05f).ShouldBe(right[^1] / 0.9f, 1e-4f);
        left[^1].ShouldBeLessThan(0.05f);
    }

    [Fact]
    public void Makeup_brings_it_back_up()
    {
        var plain = Play(Type, Hold(0.5f, 0.3), knobs: [(Attack, -3f)]).Left[^1];
        var made = Play(Type, Hold(0.5f, 0.3), knobs: [(Attack, -3f), (Makeup, 6f)]).Left[^1];

        Decibels(made / plain).ShouldBe(6d, 1e-3);
    }

    /// <summary>A quiet left and a loud key: ducking.</summary>
    [Fact]
    public void A_patched_key_is_what_it_listens_to()
    {
        var (left, gain) = Play(
            Type, Hold(0.05f, 0.5), Hold(0.9f, 0.5), into: (0, Key), heard: (0, Gain), knobs: [(Attack, -3f)]);

        left[^1].ShouldBeLessThan(0.03f);
        gain[^1].ShouldBeLessThan(0.6f);
    }

    [Fact]
    public void The_gain_is_one_until_it_has_something_to_do()
    {
        Play(Type, Hold(0.01f, 0.1), heard: (0, Gain)).Right.ShouldAllBe(g => g == 1f);
    }

    [Fact]
    public void Attack_takes_its_time()
    {
        var (_, fast) = Play(Type, Hold(0.9f, 0.5), heard: (0, Gain), knobs: [(Attack, -4f)]);
        var (_, slow) = Play(Type, Hold(0.9f, 0.5), heard: (0, Gain), knobs: [(Attack, -1.5f)]);

        var early = Rate / 200;
        fast[early].ShouldBeLessThan(slow[early]);
        fast[^1].ShouldBe(slow[^1], 1e-4f);
    }

    [Fact]
    public void On_the_picture_it_is_a_wire()
    {
        float[] values = [-0.75f, 0f, 0.5f, 1f];

        Picture(Type, values, (Threshold, -60f)).ShouldBe(values);
    }
}
