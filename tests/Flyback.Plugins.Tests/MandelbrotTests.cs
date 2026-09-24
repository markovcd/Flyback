using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.Fractals;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Mandelbrot set: a pure function of the point, so most of this is places
/// on the plane and what comes back, and the rest counts the ops an iteration
/// costs.
/// </summary>
public class MandelbrotTests
{
    [Fact]
    public void All_three_sit_together_and_a_fresh_one_carries_sixty_four_iterations()
    {
        var category = Catalog.Require(Mandelbrot).Category;

        category.ShouldBe("Fractals");
        Catalog.Require(Julia).Category.ShouldBe(category);
        Catalog.Require(Orbit).Category.ShouldBe(category);

        NodeInstance.Create(Catalog.Require(Mandelbrot), 0, 0)
            .StateOf("escape").ShouldBeOfType<JsonObject>()["iterations"]!
            .GetValue<string>().ShouldBe("64");
    }

    /// <summary>
    /// Read at the middle of the picture, so 're' and 'im' are the point itself:
    /// the origin, the main body, the bulb to its left, a point on the spike and
    /// the bulb on top never escape.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-0.2f, 0.1f)]
    [InlineData(-1f, 0f)]
    [InlineData(-1.9f, 0f)]
    [InlineData(-0.12f, 0.75f)]
    public void A_point_in_the_set_is_inside_and_black(float re, float im)
    {
        Point(Inside, re, im).ShouldBe(1d);
        Point(Escape, re, im).ShouldBe(0d);
        ColorAt(Seen(Module(Mandelbrot, 64, (Re, re), (Im, im)), Color), 0f, 0f).ShouldBe((0d, 0d, 0d));
    }

    [Theory]
    [InlineData(0.5f, 0f)]
    [InlineData(-2.5f, 0f)]
    [InlineData(0f, 1.5f)]
    [InlineData(-0.75f, 0.2f)]
    public void A_point_outside_escapes_and_is_colored(float re, float im)
    {
        Point(Inside, re, im).ShouldBe(0d);
        Point(Escape, re, im).ShouldBeInRange(0.001d, 1d);

        var (r, g, b) = ColorAt(Seen(Module(Mandelbrot, 64, (Re, re), (Im, im)), Color), 0f, 0f);
        (r + g + b).ShouldBeGreaterThan(0.05d);
    }

    [Fact]
    public void Escape_climbs_toward_the_edge()
    {
        Point(Escape, 1.5f, 0f).ShouldBeLessThan(Point(Escape, 0.5f, 0f));
        Point(Escape, 0.5f, 0f).ShouldBeLessThan(Point(Escape, 0.3f, 0f));
    }

    /// <summary>
    /// The smooth count has no bands: walking out along the real axis, where
    /// a whole count would jump by one iteration at every band, no step comes
    /// near that.
    /// </summary>
    [Fact]
    public void Escape_has_no_bands()
    {
        var steps = Enumerable.Range(0, 400).Select(i => Point(Escape, 0.5f + i * 0.005f, 0f)).ToList();

        var largest = steps.Zip(steps.Skip(1), (a, b) => Math.Abs(a - b)).Max();

        largest.ShouldBeLessThan(1d / 64 / 4);
        (steps[0] - steps[^1]).ShouldBeGreaterThan(3d / 64);
    }

    [Theory]
    [InlineData(0.3f, 0.2f)]
    [InlineData(-0.9f, 0.5f)]
    [InlineData(1.1f, -0.7f)]
    public void A_step_of_zoom_halves_the_view(float x, float y)
    {
        var near = Field(Mandelbrot, Escape, 64, (Re, -0.5f), (Im, 0.4f), (Zoom, 1f));
        var far = Field(Mandelbrot, Escape, 64, (Re, -0.5f), (Im, 0.4f));

        near(x, y).ShouldBe(far(x / 2f, y / 2f), 1e-6);
    }

    /// <summary>
    /// Nothing a socket can be turned to makes a value no color means: far past
    /// the knob's zoom, a middle nowhere near the set, a shift far round.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 40f, 0f)]
    [InlineData(1e30f, -1e30f, 0f, 0f)]
    [InlineData(-0.75f, 0.1f, -40f, 1e6f)]
    [InlineData(-0.7436439f, 0.1318259f, 12f, -3f)]
    public void Every_reading_is_a_finite_number_in_range(float re, float im, float zoom, float shift)
    {
        (int, float)[] knobs = [(Re, re), (Im, im), (Zoom, zoom), (Shift, shift)];

        foreach (var port in new[] { Escape, Inside })
        {
            var field = Field(Mandelbrot, port, 256, knobs);

            foreach (var (x, y) in Grid()) field(x, y).ShouldBeInRange(0d, 1d);
        }

        var color = Seen(Module(Mandelbrot, 256, knobs), Color);

        foreach (var (x, y) in Grid())
        {
            var (r, g, b) = ColorAt(color, x, y);

            foreach (var channel in new[] { r, g, b }) channel.ShouldBeInRange(0d, 1d);
        }
    }

    [Fact]
    public void An_iteration_costs_about_a_dozen_ops()
    {
        var perIteration = (Seen(Module(Mandelbrot, 128), Color).Ops.Length
            - Seen(Module(Mandelbrot, 64), Color).Ops.Length) / 64d;

        perIteration.ShouldBeInRange(10d, 14d);
    }

    [Theory]
    [InlineData("1000", 256)]
    [InlineData("20", 16)]
    [InlineData("100", 128)]
    [InlineData("many", 64)]
    [InlineData(null, 64)]
    public void A_count_the_module_cannot_run_is_held_to_one_it_can(string? stored, int iterations)
    {
        var node = Module(Mandelbrot);
        node.State = stored is null ? null : new() { ["escape"] = new JsonObject { ["iterations"] = stored } };

        Seen(node, Escape).Ops.Length.ShouldBe(Seen(Module(Mandelbrot, iterations), Escape).Ops.Length);
    }

    [Fact]
    public void It_keeps_nothing_and_survives_to_the_shader()
    {
        var program = Seen(Module(Mandelbrot, 64), Color);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();

        LowersToEveryDialect(program);
    }

    /// <summary>Pure, so the speakers read the same set the screen draws.</summary>
    [Fact]
    public void The_speakers_hear_the_set_the_screen_draws()
    {
        var heard = Heard(Module(Mandelbrot, 64), Escape);
        var registers = heard.AllocateRegisters();
        var seen = Field(Mandelbrot, Escape, 64);

        foreach (var x in new[] { -1.7f, -0.3f, 0f, 0.62f, 1.4f })
        {
            heard.Evaluate(x, 0d, 0d, registers, default);
            registers[heard.OutputBase].ShouldBe(seen(x, 0f), 1e-9);
        }
    }

    [Fact]
    public void Dive_compiles_clean_at_a_hundred_and_twenty_eight_iterations()
    {
        var loaded = ShippedPlugins.Loaded;
        var patch = loaded.Presets.Single(p => p.Name == "Dive").Build(loaded.Modules);

        var video = patch.CompileForVideo(loaded.Modules);

        video.Issues.ShouldBeEmpty();
        patch.CompileForAudio(loaded.Modules).Issues.ShouldBeEmpty();

        var alone = Seen(Module(Mandelbrot, 128), Color).Ops.Length;
        video.Program.Ops.Length.ShouldBeInRange(alone, alone + 20);
    }

    private static double Point(int port, float re, float im) => Field(Mandelbrot, port, 64, (Re, re), (Im, im))(0f, 0f);
}
