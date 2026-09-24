using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Fractals;

namespace Flyback.Plugins.Tests;

/// <summary>The orbit of c: stepped at a rate and heard, and drawn as the path it takes.</summary>
public class OrbitTests
{
    private const int OrbitRe = 2;
    private const int OrbitIm = 3;
    private const int OrbitRate = 4;

    private const int Left = 0;
    private const int Right = 1;
    private const int Trace = 3;

    /// <summary>
    /// A cycle of n steps at a rate r is a tone at r over n: the rabbit's orbit
    /// settles into three, c at minus one alternates, and a c outside the set
    /// escapes on its sixth step and starts again.
    /// </summary>
    [Theory]
    [InlineData(-0.12f, 0.75f, 3)]
    [InlineData(-1f, 0f, 2)]
    [InlineData(0.5f, 0f, 6)]
    public void An_orbit_of_n_steps_repeats_every_n_steps(float re, float im, int steps)
    {
        const float rate = 300f;

        var sound = Played(re, im, rate, Left);
        var period = (int)Math.Round(steps * Rate / rate);

        var settled = sound.AsSpan(Rate / 2);
        var shifted = settled[period..];

        Rms(Difference(settled[..shifted.Length], shifted)).ShouldBeLessThan(Rms(settled) * 0.05);

        // And not at any shorter whole number of steps.
        for (var fewer = 1; fewer < steps; fewer++)
        {
            var lag = (int)Math.Round(fewer * Rate / rate);
            Rms(Difference(settled[..(settled.Length - lag)], settled[lag..])).ShouldBeGreaterThan(Rms(settled) * 0.2);
        }
    }

    [Theory]
    [InlineData(-0.12f, 0.75f)]
    [InlineData(-1.4f, 0.02f)]
    [InlineData(0.3f, 0f)]
    public void Both_sides_are_centered_and_inside_the_rails(float re, float im)
    {
        foreach (var port in new[] { Left, Right })
        {
            var sound = Played(re, im, 330f, port).AsSpan(Rate / 2);

            foreach (var sample in sound) sample.ShouldBeInRange(-1f, 1f);

            var mean = 0d;
            foreach (var sample in sound) mean += sample;

            Math.Abs(mean / sound.Length).ShouldBeLessThan(0.02);
        }
    }

    [Fact]
    public void A_rate_of_nought_is_silence()
    {
        var sound = Played(-0.12f, 0.75f, 0f, Left).AsSpan(Rate / 2);

        Rms(sound).ShouldBeLessThan(1e-3);
    }

    /// <summary>
    /// The path is drawn on the Mandelbrot's plane at rest: lit at c, lit at the
    /// orbit's next point, and dark well away from both.
    /// </summary>
    [Fact]
    public void The_trace_is_lit_along_the_orbit_and_dark_off_it()
    {
        const float re = -0.12f, im = 0.75f;

        var trace = Field(Orbit, Trace, 0, (OrbitRe, re), (OrbitIm, im));

        // c² + c.
        var (nextRe, nextIm) = (re * re - im * im + re, 2 * re * im + im);

        trace(ScreenX(re), ScreenY(im)).ShouldBe(1d, 1e-6);
        trace(ScreenX(nextRe), ScreenY(nextIm)).ShouldBe(1d, 1e-6);
        trace(ScreenX(0.4f), ScreenY(-1f)).ShouldBe(0d);
    }

    [Fact]
    public void Its_picture_survives_to_the_shader()
    {
        LowersToEveryDialect(Seen(Module(Orbit), Color));
    }

    [Fact]
    public void Julia_walk_plays_and_draws_clean()
    {
        var loaded = ShippedPlugins.Loaded;
        var patch = loaded.Presets.Single(p => p.Name == "Julia walk").Build(loaded.Modules);

        patch.CompileForVideo(loaded.Modules).Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        Rms(Play(audio.Program, 1d).AsSpan(Rate / 2)).ShouldBeGreaterThan(0.01);
    }

    private static float[] Played(float re, float im, float rate, int port) =>
        Play(Heard(Module(Orbit, 0, (OrbitRe, re), (OrbitIm, im), (OrbitRate, rate)), port), 1.5d);

    private static float ScreenX(float re) => (re + 0.75f) / 1.25f;

    private static float ScreenY(float im) => im / 1.25f;

    private static float[] Difference(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var difference = new float[a.Length];
        for (var i = 0; i < a.Length; i++) difference[i] = a[i] - b[i];
        return difference;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0d;
        foreach (var s in samples) sum += s * (double)s;
        return Math.Sqrt(sum / samples.Length);
    }
}
