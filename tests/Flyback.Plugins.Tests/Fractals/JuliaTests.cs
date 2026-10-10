using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Fractals;

namespace Flyback.Plugins.Tests;

/// <summary>The Julia set: the Mandelbrot's arithmetic with the start and the constant swapped.</summary>
public class JuliaTests
{
    /// <summary>Half the picture's height on the plane at zoom nought.</summary>
    private const float Span = 1.2f;

    /// <summary>With c at nought, z just squares, so the set is the unit disc.</summary>
    [Theory]
    [InlineData(0.5f, 0f, 1d)]
    [InlineData(0f, -0.9f, 1d)]
    [InlineData(1.1f, 0f, 0d)]
    [InlineData(-1f, 1f, 0d)]
    public void With_c_at_nought_the_set_is_the_unit_disc(float re, float im, double inside)
    {
        Field(Julia, Inside, 64, (Re, 0f), (Im, 0f))(re / Span, im / Span).ShouldBe(inside);
    }

    /// <summary>
    /// Started at nought, a Julia point runs exactly the Mandelbrot's orbit, so
    /// the middle of a Julia picture is inside exactly when its c is in the
    /// Mandelbrot set.
    /// </summary>
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(-0.12f, 0.75f)]
    [InlineData(0.5f, 0f)]
    [InlineData(-0.8f, 0.156f)]
    [InlineData(0.3f, -0.6f)]
    public void Its_middle_is_inside_exactly_where_c_is_in_the_mandelbrot_set(float re, float im)
    {
        var julia = Field(Julia, Inside, 64, (Re, re), (Im, im))(0f, 0f);
        var mandelbrot = Field(Mandelbrot, Inside, 64, (Re, re), (Im, im))(0f, 0f);

        julia.ShouldBe(mandelbrot);
    }

    /// <summary>z and -z square to the same thing, so every Julia set is symmetric through its middle.</summary>
    [Fact]
    public void It_is_the_same_turned_half_round()
    {
        var escape = Field(Julia, Escape, 64);

        foreach (var (x, y) in Grid()) escape(x, y).ShouldBe(escape(-x, -y), 1e-9);
    }

    [Theory]
    [InlineData(0f, 0f, 40f)]
    [InlineData(1e30f, -1e30f, 0f)]
    [InlineData(-0.8f, 0.156f, -40f)]
    public void Every_reading_is_a_finite_number_in_range(float re, float im, float zoom)
    {
        foreach (var port in new[] { Escape, Inside })
        {
            var field = Field(Julia, port, 128, (Re, re), (Im, im), (Zoom, zoom));

            foreach (var (x, y) in Grid()) field(x, y).ShouldBeInRange(0d, 1d);
        }
    }

    [Fact]
    public void It_keeps_nothing_and_survives_to_the_shader()
    {
        var program = Seen(Module(Julia, 64), Color);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);

        LowersToEveryDialect(program);
    }
}
