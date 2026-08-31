using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The two noises: the same field summed at several sizes, and the distance to
/// a set of scattered points.
/// </summary>
/// <remarks>
/// Both are pure functions of a position, so most of this is a table of places
/// and the numbers that come back. What is not is the Fractal's octave count,
/// which is carried on the node rather than wired into it — so it is the one
/// thing here that changes the length of the program rather than its answer, and
/// several of these count ops rather than reading them.
/// </remarks>
public class NoiseTests
{
    private const string Fractal = "flyback.picture.fractal";
    private const string Cells = "flyback.picture.cells";
    private const string Noise = "pattern.noise";

    private const int Scale = 3;
    private const int Roughness = 4;
    private const int Jitter = 4;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    // --- the catalogue ---------------------------------------------------------

    [Fact]
    public void The_plugin_offers_both_fields_from_one_assembly()
    {
        Catalog.Get(Fractal).ShouldNotBeNull().Name.ShouldBe("Fractal");
        Catalog.Get(Cells).ShouldNotBeNull().Name.ShouldBe("Cells");

        Catalog.ProviderOf(Fractal).ShouldBe(Catalog.ProviderOf(Cells));

        // The category the engine's own Noise is in, rather than one of their
        // own: these are the same kind of thing and belong beside it.
        Catalog.Require(Fractal).Category.ShouldBe(Catalog.Require(Noise).Category);
    }

    /// <summary>
    /// The count is a thing the instance carries, so a fresh one carries a
    /// sensible number of them and a saved patch carries whatever was chosen.
    /// It is the first shipped plugin to carry anything at all.
    /// </summary>
    [Fact]
    public void A_fresh_fractal_carries_four_octaves()
    {
        var node = NodeInstance.Create(Catalog.Require(Fractal), 0, 0);

        node.StateOf("fractal").ShouldBeOfType<JsonObject>()["octaves"]!
            .GetValue<string>().ShouldBe("4");

        Catalog.Require(Cells).Extras.ShouldBeEmpty();
    }

    // --- the fractal -----------------------------------------------------------

    /// <summary>
    /// One octave is the module it is built out of, exactly — which is what says
    /// the sum, the signing and the normalising all cancel where there is nothing
    /// to sum. Read where z is nothing, because that is the one place the two
    /// agree about it: this scales z with the picture and the Noise does not.
    /// </summary>
    [Fact]
    public void One_octave_is_exactly_the_noise_it_is_built_from()
    {
        var one = Field(Fractal, 0, 1, (Scale, 3f));
        var plain = Field(Noise, 0, 0, (Scale, 3f));

        foreach (var (x, y) in Grid())
            one(x, y).ShouldBe(plain(x, y), 1e-9);
    }

    /// <summary>
    /// What the count decides is how long the program is, and this is the whole
    /// argument for its not being a socket: one noise lookup an octave, and noise
    /// is far and away the dearest op in the machine.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public void An_octave_costs_exactly_one_noise(int octaves)
    {
        Program(Fractal, 0, octaves).Ops.Count(op => op.Code == OpCode.Noise3).ShouldBe(octaves);
    }

    /// <summary>
    /// A patch is text somebody may have edited, and a stored count that means
    /// nothing has to build something rather than nothing. Held to what the
    /// module can do where it is a number, and back to the default where it is
    /// not one at all — including a module carrying no state, which is what a
    /// patch saved before this module had any would look like.
    /// </summary>
    [Theory]
    [InlineData("40", 8)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("three", 4)]
    [InlineData("", 4)]
    [InlineData(null, 4)]
    public void A_count_the_module_cannot_build_is_held_to_one_it_can(string? stored, int octaves)
    {
        var patch = new Patch();

        var node = NodeInstance.Create(Catalog.Require(Fractal), 0, 0);
        node.State = stored is null ? null : new() { ["fractal"] = new JsonObject { ["octaves"] = stored } };
        patch.Nodes.Add(node);

        var screen = NodeInstance.Create(Catalog.Require(NodeCatalog.OutputTypeId), 0, 0);
        patch.Nodes.Add(screen);
        patch.Connect(node.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        patch.CompileForVideo(Catalog).Program.Ops
            .Count(op => op.Code == OpCode.Noise3).ShouldBe(octaves);
    }

    /// <summary>
    /// Roughness is how much of the previous octave each one keeps, so at nothing
    /// the finer ones contribute nothing and the field is the first octave alone.
    /// It still pays for them, which is the honest half of the same fact: the
    /// count is what decides the cost, and roughness is only what the sum does.
    /// </summary>
    [Fact]
    public void Roughness_at_nothing_leaves_the_first_octave_alone()
    {
        var flat = Field(Fractal, 0, 6, (Scale, 3f), (Roughness, 0f));
        var single = Field(Fractal, 0, 1, (Scale, 3f));

        foreach (var (x, y) in Grid()) flat(x, y).ShouldBe(single(x, y), 1e-9);

        Program(Fractal, 0, 6, (Roughness, 0f)).Ops
            .Count(op => op.Code == OpCode.Noise3).ShouldBe(6);
    }

    /// <summary>
    /// Adding octaves adds detail rather than brightness. Both readings stay
    /// inside 0 to 1 whatever is asked of them, which is what lets either drive a
    /// color without a Clamp after it.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(4f)]
    [InlineData(-2f)]
    public void Neither_reading_ever_leaves_nought_to_one(float roughness)
    {
        var smooth = Field(Fractal, 0, 8, (Scale, 5f), (Roughness, roughness));
        var folded = Field(Fractal, 1, 8, (Scale, 5f), (Roughness, roughness));

        foreach (var (x, y) in Grid(9))
        {
            smooth(x, y).ShouldBeInRange(0d, 1d);
            folded(x, y).ShouldBeInRange(0d, 1d);
        }
    }

    /// <summary>
    /// More octaves is more detail, measured as the field disagreeing with itself
    /// over a short step. A sum that had gone flat or been washed out by
    /// normalising would fail this and pass everything above it.
    /// </summary>
    [Fact]
    public void More_octaves_puts_more_detail_in()
    {
        Roughness_of(Field(Fractal, 0, 6, (Scale, 2f)))
            .ShouldBeGreaterThan(Roughness_of(Field(Fractal, 0, 1, (Scale, 2f))) * 1.5);
    }

    // --- the cells -------------------------------------------------------------

    /// <summary>
    /// With the scatter turned off every point sits in the middle of its square,
    /// so the field is the distance to the nearest centre of a unit grid — which
    /// is a number this test can work out for itself rather than measure.
    /// </summary>
    [Theory]
    [InlineData(0.5f, 0.5f, 0f)]
    [InlineData(0f, 0f, 0.70710678f)]
    [InlineData(0.5f, 0f, 0.5f)]
    [InlineData(1.5f, 2.5f, 0f)]
    [InlineData(-0.5f, -0.5f, 0f)]
    [InlineData(0.25f, 0.5f, 0.25f)]
    public void An_unjittered_grid_measures_to_the_nearest_square(float x, float y, float expected)
    {
        Field(Cells, 0, 0, (Scale, 1f), (Jitter, 0f))(x, y).ShouldBe(expected, 1e-5);
    }

    /// <summary>
    /// And the second nearest is a square away, so the edge reading is nothing
    /// exactly on the line between two of them — which is what makes it a crack.
    /// </summary>
    [Fact]
    public void The_edge_reading_goes_to_nothing_between_two_cells()
    {
        var edge = Field(Cells, 1, 0, (Scale, 1f), (Jitter, 0f));

        // Halfway between the centres of two neighbouring squares.
        edge(1f, 0.5f).ShouldBe(0d, 1e-5);
        edge(0.5f, 1f).ShouldBe(0d, 1e-5);

        // And is at its largest at a centre, where the runner-up is furthest.
        edge(0.5f, 0.5f).ShouldBeGreaterThan(0.9d);
    }

    /// <summary>
    /// The one value here that is flat across a region and jumps at its border,
    /// which is what nothing else in the catalogue can make.
    /// </summary>
    [Fact]
    public void A_cell_reads_the_same_everywhere_inside_it_and_differently_next_door()
    {
        var cell = Field(Cells, 2, 0, (Scale, 1f), (Jitter, 0f));

        var mine = cell(0.5f, 0.5f);

        cell(0.2f, 0.3f).ShouldBe(mine, 1e-9);
        cell(0.8f, 0.7f).ShouldBe(mine, 1e-9);

        cell(1.5f, 0.5f).ShouldNotBe(mine);
        cell(0.5f, 1.5f).ShouldNotBe(mine);
    }

    [Fact]
    public void Every_cells_reading_stays_inside_the_range_it_declares()
    {
        var distance = Field(Cells, 0, 0, (Scale, 4f));
        var edge = Field(Cells, 1, 0, (Scale, 4f));
        var cell = Field(Cells, 2, 0, (Scale, 4f));

        foreach (var (x, y) in Grid(11))
        {
            distance(x, y).ShouldBeInRange(0d, 1.5d);
            edge(x, y).ShouldBeInRange(0d, 1.5d);
            cell(x, y).ShouldBeInRange(0d, 1d);
        }
    }

    /// <summary>
    /// The price, stated as a number so that it cannot quietly change: nine
    /// squares, two lookups each, because the only agreed randomness in the
    /// machine is Noise and a point needs two coordinates.
    /// </summary>
    [Fact]
    public void A_cell_field_costs_eighteen_noise_lookups()
    {
        Program(Cells, 0, 0).Ops.Count(op => op.Code == OpCode.Noise3).ShouldBe(18);
    }

    // --- both sinks and the shader ---------------------------------------------

    [Theory]
    [InlineData(Fractal)]
    [InlineData(Cells)]
    public void Both_fields_survive_to_the_shader(string typeId)
    {
        var program = Program(typeId, 0, 4);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
        program.Tables.Count.ShouldBe(0);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldContain("nz(");
    }

    /// <summary>
    /// Pure, so the speakers and the screen run the same arithmetic — which is
    /// what makes a Fractal read at the origin a usable slow wander for the ear
    /// as well as a field for the eye.
    /// </summary>
    [Theory]
    [InlineData(Fractal)]
    [InlineData(Cells)]
    public void Both_fields_are_the_same_module_at_both_sinks(string typeId)
    {
        var seen = Field(typeId, 0, 4);
        var heard = Heard(typeId);

        foreach (var x in new[] { -0.8f, -0.2f, 0f, 0.37f, 1.4f })
            heard(x).ShouldBe(seen(x, 0f), 1e-9);
    }

    // --- the preset ------------------------------------------------------------

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Marble").Build(loaded.Modules);

        patch.Nodes.Count(n => n.TypeId == Fractal).ShouldBe(2);

        var video = patch.CompileForVideo(loaded.Modules);
        var audio = patch.CompileForAudio(loaded.Modules);

        video.Issues.ShouldBeEmpty();
        audio.Issues.ShouldBeEmpty();

        // Three octaves on the warp and five on the veins, set on the nodes — so
        // the picture pays for eight noise lookups rather than for the sixteen
        // two default Fractals would have cost.
        video.Program.Ops.Count(op => op.Code == OpCode.Noise3).ShouldBe(8);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(video.Program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    // --- harness ---------------------------------------------------------------

    /// <summary>One output of one module, as a function of where it is read.</summary>
    private static Func<float, float, double> Field(
        string typeId, int port, int octaves, params (int Port, float Value)[] knobs)
    {
        var program = Program(typeId, port, octaves, knobs);
        var registers = program.AllocateRegisters();

        return (x, y) =>
        {
            program.Evaluate(x, y, 0d, registers, default);
            return registers[program.OutputBase];
        };
    }

    /// <summary>The same module compiled into the speakers, read through x.</summary>
    private static Func<float, double> Heard(string typeId)
    {
        var patch = new Patch();

        var node = Add(patch, typeId, 4);
        var sink = Add(patch, NodeCatalog.OutputTypeId, 0, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(node.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        return x =>
        {
            program.Evaluate(x, 0d, 0d, registers, default);
            return registers[program.OutputBase];
        };
    }

    private static CompiledPatch Program(
        string typeId, int port, int octaves, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var node = Add(patch, typeId, octaves, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId, 0);

        patch.Connect(node.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }

    private static NodeInstance Add(
        Patch patch, string typeId, int octaves, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        if (octaves > 0)
            node.SetState("fractal", new JsonObject { ["octaves"] = octaves.ToString() });

        patch.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// How much the field disagrees with itself over a short step, summed — which
    /// is detail, measured without knowing what the field is supposed to look
    /// like.
    /// </summary>
    private static double Roughness_of(Func<float, float, double> field)
    {
        const float step = 0.01f;

        var total = 0d;
        foreach (var (x, y) in Grid(21)) total += Math.Abs(field(x + step, y) - field(x, y));

        return total;
    }

    /// <summary>Points across the picture, off the axes so none of them is a special case.</summary>
    private static IEnumerable<(float X, float Y)> Grid(int across = 7)
    {
        for (var i = 0; i < across; i++)
        for (var j = 0; j < across; j++)
            yield return (
                -1.3f + 2.6f * (i + 0.31f) / across,
                -0.9f + 1.8f * (j + 0.17f) / across);
    }
}
