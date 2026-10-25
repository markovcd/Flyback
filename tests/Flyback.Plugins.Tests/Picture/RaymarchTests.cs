using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Picture;

/// <summary>
/// The Raymarch: a ray from each pixel walked into a scene until it lands. Pure,
/// so most of this is pixels aimed at places in the scene and how far their rays
/// went; the rest counts what a step of the march costs.
/// </summary>
public class RaymarchTests
{
    private const string Raymarch = "flyback.picture.raymarch";

    private const int Travel = 2;
    private const int Twist = 4;
    private const int Pulse = 5;
    private const int Morph = 6;
    private const int Spacing = 7;
    private const int Fog = 9;
    private const int Hue = 10;

    private const int Color = 0;
    private const int Depth = 1;
    private const int Hit = 2;
    private const int Glow = 3;
    private const int Shade = 4;

    /// <summary>How far a ray is followed before it counts as sky.</summary>
    private const double Far = 40d;

    /// <summary>The screen's x and y that aim a ray at a direction, as the lens spreads them.</summary>
    private const float Lens = 0.9f;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    private static readonly string[] Scenes = ["lattice", "tunnel", "gyroid"];

    [Fact]
    public void A_fresh_one_flies_through_the_lattice_in_sixty_four_steps()
    {
        Catalog.Require(Raymarch).Category.ShouldBe(Catalog.Require("pattern.clouds").Category);

        var state = NodeInstance.Create(Catalog.Require(Raymarch), 0, 0).StateOf("march").ShouldBeOfType<JsonObject>();

        state["scene"]!.GetValue<string>().ShouldBe("lattice");
        state["steps"]!.GetValue<string>().ShouldBe("64");
    }

    /// <summary>
    /// The camera's axis runs down the gap between the lattice's columns, the
    /// tunnel's middle and a line the gyroid never crosses, so the ray from the
    /// middle of the picture finds nothing in any of them.
    /// </summary>
    [Theory]
    [InlineData("lattice")]
    [InlineData("tunnel")]
    [InlineData("gyroid")]
    public void The_middle_of_the_picture_looks_down_a_clear_path(string scene)
    {
        var at = Reader(scene, 64, (Twist, 0f));

        at(Hit, 0f, 0f).ShouldBe(0d);
        at(Depth, 0f, 0f).ShouldBe(1d);
    }

    /// <summary>
    /// A ray aimed at the middle of the cell up, right and one along lands on its
    /// ball, a radius short of the middle: a fifth of the spacing.
    /// </summary>
    [Fact]
    public void A_ray_aimed_at_a_ball_lands_a_radius_short_of_its_middle()
    {
        var (x, y) = Aim(1.5f, 1.5f, 3f);
        var at = Reader("lattice", 64, (Twist, 0f));

        at(Hit, x, y).ShouldBe(1d);
        at(Depth, x, y).ShouldBe((Math.Sqrt(1.5 * 1.5 * 2 + 3 * 3) - 0.6) / Far, 0.01 / Far);
        at(Shade, x, y).ShouldBeGreaterThan(0.2d);
    }

    [Fact]
    public void A_pulse_swells_the_ball_towards_the_camera()
    {
        var (x, y) = Aim(1.5f, 1.5f, 3f);

        var swollen = Reader("lattice", 64, (Twist, 0f), (Pulse, 1f))(Depth, x, y);

        swollen.ShouldBe((Math.Sqrt(1.5 * 1.5 * 2 + 3 * 3) - 0.96) / Far, 0.01 / Far);
    }

    [Fact]
    public void Morph_turns_the_ball_into_a_box_that_reaches_further()
    {
        var (x, y) = Aim(1.5f, 1.5f, 3f);

        var ball = Reader("lattice", 64, (Twist, 0f))(Depth, x, y);
        var box = Reader("lattice", 64, (Twist, 0f), (Morph, 1f))(Depth, x, y);

        box.ShouldBeLessThan(ball);
    }

    /// <summary>The lattice repeats every 'spacing' along the flight, so flying one cell on is where it started.</summary>
    [Fact]
    public void Travelling_one_spacing_returns_to_the_same_view()
    {
        var (x, y) = Aim(1.5f, 1.5f, 3f);

        var start = Reader("lattice", 64, (Twist, 0f))(Depth, x, y);

        Reader("lattice", 64, (Twist, 0f), (Travel, 3f))(Depth, x, y).ShouldBe(start, 1e-6);
        Math.Abs(Reader("lattice", 64, (Twist, 0f), (Travel, 1.5f))(Depth, x, y) - start).ShouldBeGreaterThan(1e-3);
    }

    [Fact]
    public void The_tunnel_wall_is_found_off_to_the_side()
    {
        var at = Reader("tunnel", 64, (Twist, 0f));

        at(Hit, 1.7f, 0f).ShouldBe(1d);
        at(Depth, 1.7f, 0f).ShouldBeLessThan(at(Depth, 0.2f, 0f));
    }

    /// <summary>
    /// A ray that ran out of steps on its way is sky. With too few steps to get
    /// there, a ray that lands at 64 lands nowhere.
    /// </summary>
    [Fact]
    public void Fewer_steps_reach_less_far()
    {
        var x = 0.5f;

        Reader("tunnel", 64, (Twist, 0f))(Hit, x, 0f).ShouldBe(1d);
        Reader("tunnel", 16, (Twist, 0f))(Depth, x, 0f).ShouldBeGreaterThanOrEqualTo(
            Reader("tunnel", 64, (Twist, 0f))(Depth, x, 0f));
    }

    /// <summary>Nothing a socket can be turned to makes a value no color means, in any scene.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f, 0f, 3f, 0.5f, 0f)]
    [InlineData(1e6f, 1f, 1f, 1f, 8f, 1f, 1f)]
    [InlineData(-50f, -1f, -3f, 4f, 0f, 0f, -2f)]
    [InlineData(37.5f, 0.6f, 0.4f, -1f, 1e5f, 1e3f, 1e6f)]
    public void Every_reading_is_a_finite_number_in_range(
        float travel, float twist, float pulse, float morph, float spacing, float fog, float hue)
    {
        (int, float)[] knobs = [(Travel, travel), (Twist, twist), (Pulse, pulse), (Morph, morph), (Spacing, spacing), (Fog, fog), (Hue, hue)];

        foreach (var scene in Scenes)
        {
            var program = Seen(Module(scene, 16, knobs), Color);
            var at = Reader(scene, 16, knobs);

            foreach (var (x, y) in Grid())
            {
                foreach (var channel in Rgb(program, x, y)) channel.ShouldBeInRange(0d, 1d);
                foreach (var port in new[] { Depth, Hit, Glow, Shade }) at(port, x, y).ShouldBeInRange(0d, 1d);
            }
        }
    }

    /// <summary>The march has no loop to run, so each step is the scene's distance function written out again.</summary>
    [Theory]
    [InlineData("lattice")]
    [InlineData("tunnel")]
    [InlineData("gyroid")]
    public void A_step_costs_the_scene_once_more(string scene)
    {
        var perStep = (Seen(Module(scene, 128), Color).Ops.Length - Seen(Module(scene, 64), Color).Ops.Length) / 64d;

        perStep.ShouldBeInRange(40d, 55d);
    }

    [Theory]
    [InlineData("1000", 256)]
    [InlineData("20", 16)]
    [InlineData("100", 128)]
    [InlineData("many", 64)]
    public void A_count_it_cannot_run_is_held_to_one_it_can(string stored, int steps)
    {
        var node = Module("lattice", 0);
        node.SetState("march", new JsonObject { ["scene"] = "lattice", ["steps"] = stored });

        Seen(node, Color).Ops.Length.ShouldBe(Seen(Module("lattice", steps), Color).Ops.Length);
    }

    [Fact]
    public void A_scene_it_does_not_know_is_the_lattice()
    {
        Seen(Module("moon", 32), Color).Ops.Length.ShouldBe(Seen(Module("lattice", 32), Color).Ops.Length);
    }

    [Theory]
    [InlineData("lattice")]
    [InlineData("tunnel")]
    [InlineData("gyroid")]
    public void It_keeps_nothing_and_survives_to_the_shader(string scene)
    {
        var program = Seen(Module(scene, 64), Color);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
        program.Tables.Count.ShouldBe(0);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Flythrough_compiles_clean_and_its_picture_is_one_march()
    {
        var loaded = ShippedPlugins.Loaded;
        var patch = loaded.Presets.Single(p => p.Name == "Flythrough").Build(loaded.Modules);

        var video = patch.CompileForVideo(loaded.Modules);

        video.Issues.ShouldBeEmpty();
        patch.CompileForAudio(loaded.Modules).Issues.ShouldBeEmpty();

        var alone = Seen(Module("lattice", 64), Color).Ops.Length;
        video.Program.Ops.Length.ShouldBeInRange(alone, alone + 200);
    }

    /// <summary>The screen's x and y whose ray points at a place in the camera's frame.</summary>
    private static (float X, float Y) Aim(float x, float y, float z) => (x / z / Lens, y / z / Lens);

    private static NodeInstance Module(string scene, int steps, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(Raymarch), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        if (steps > 0) node.SetState("march", new JsonObject { ["scene"] = scene, ["steps"] = steps.ToString() });

        return node;
    }

    /// <summary>One scalar output of a Raymarch, read at a point of the screen.</summary>
    private static Func<int, float, float, double> Reader(string scene, int steps, params (int Port, float Value)[] knobs)
    {
        var programs = new Dictionary<int, CompiledPatch>();

        return (port, x, y) =>
        {
            if (!programs.TryGetValue(port, out var program))
                programs[port] = program = Seen(Module(scene, steps, knobs), port);

            var registers = program.AllocateRegisters();
            program.Evaluate(x, y, 0d, registers, default);

            return registers[program.OutputBase];
        };
    }

    private static double[] Rgb(CompiledPatch program, float x, float y)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default);

        return [registers[program.OutputBase], registers[program.OutputBase + 1], registers[program.OutputBase + 2]];
    }

    private static CompiledPatch Seen(NodeInstance node, int port)
    {
        var patch = new Patch();
        patch.Nodes.Add(node);

        var sink = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        patch.Nodes.Add(sink);
        patch.Connect(node.Id, port, sink.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }

    /// <summary>Points across the picture, off the axes so none is a special case.</summary>
    private static IEnumerable<(float X, float Y)> Grid()
    {
        for (var i = 0; i < 5; i++)
            for (var j = 0; j < 5; j++)
                yield return (-1.7f + i * 0.83f, -0.95f + j * 0.47f);
    }
}
