using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The six Form modules, loaded off disk and read the way the renderer reads
/// them: one evaluation per point, with no state behind any of it.
/// </summary>
/// <remarks>
/// Nothing here goes through an oscillator or a clock, unlike the other plugin
/// tests in this folder, because nothing in this plugin has a memory to fill. A
/// shape is a function of x and y, so a test of one is a table of positions and
/// the numbers that come back — which is also why several of these measure the
/// <em>slope</em> rather than a value. A field that is out by a constant still
/// fills correctly and outlines wrongly, and the slope is what catches it.
/// </remarks>
public class ShapesTests
{
    private const string CircleType = "flyback.picture.circle";
    private const string BoxType = "flyback.picture.box";
    private const string PolygonType = "flyback.picture.polygon";
    private const string StarType = "flyback.picture.star";
    private const string CombineType = "flyback.picture.combine";
    private const string FillType = "flyback.picture.fill";

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    // --- the catalogue ---------------------------------------------------------

    [Fact]
    public void The_plugin_offers_all_six_modules_from_one_assembly()
    {
        string[] all = [CircleType, BoxType, PolygonType, StarType, CombineType, FillType];

        foreach (var typeId in all)
        {
            Catalog.Get(typeId).ShouldNotBeNull();
            Catalog.ProviderOf(typeId).ShouldBe(Catalog.ProviderOf(CircleType));
        }

        Catalog.Require(StarType).Category.ShouldBe(ModuleCategories.Forms);
    }

    /// <summary>
    /// Every position socket reads the pixel's own place with no wire, which is
    /// what makes a shape dropped on the canvas already a shape rather than two
    /// wires away from one.
    /// </summary>
    [Fact]
    public void A_shape_sits_in_the_middle_of_the_picture_with_nothing_patched_in()
    {
        foreach (var typeId in new[] { CircleType, BoxType, PolygonType, StarType })
        {
            var def = Catalog.Require(typeId);

            Catalog.Normalled(def.Inputs[0]).ShouldBe("Coordinates x");
            Catalog.Normalled(def.Inputs[1]).ShouldBe("Coordinates y");
        }
    }

    // --- the circle ------------------------------------------------------------

    [Theory]
    [InlineData(0f, 0f, -0.5f)]
    [InlineData(0.5f, 0f, 0f)]
    [InlineData(0f, -0.5f, 0f)]
    [InlineData(0.3f, 0.4f, 0f)]
    [InlineData(1f, 0f, 0.5f)]
    [InlineData(0.25f, 0f, -0.25f)]
    public void A_circle_is_the_distance_to_its_rim(float x, float y, float expected)
    {
        Shape(CircleType, (2, 0.5f)).At(x, y).ShouldBe(expected, 1e-6);
    }

    // --- the box ---------------------------------------------------------------

    /// <summary>
    /// Half-sizes, so a box of 0.5 and a circle of 0.5 reach the same point on
    /// the axis — the property that lets one be swapped for the other in a patch
    /// without every number around it changing.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, -0.25f)]        // the nearest wall, which is the short one
    [InlineData(0.5f, 0f, 0f)]
    [InlineData(0f, 0.25f, 0f)]
    [InlineData(0.7f, 0f, 0.2f)]        // straight out from an edge
    [InlineData(0.3f, 0.1f, -0.15f)]    // inside, nearer the top than the side
    [InlineData(0.9f, 0.65f, 0.5657f)]  // past a corner, so both axes count
    public void A_box_reaches_as_far_as_its_half_sizes_say(float x, float y, float expected)
    {
        Shape(BoxType, (2, 0.5f), (3, 0.25f)).At(x, y).ShouldBe(expected, 1e-4);
    }

    /// <summary>
    /// A corner radius at the shorter half-side leaves no straight edge, and a
    /// square with no straight edge is a disc. That is the one case where the two
    /// modules have to agree exactly, and it is the whole of what rounding is:
    /// the same field, built smaller and grown back.
    /// </summary>
    [Fact]
    public void A_fully_rounded_square_is_a_circle()
    {
        var disc = Shape(CircleType, (2, 0.4f));
        var box = Shape(BoxType, (2, 0.4f), (3, 0.4f), (4, 0.4f));

        foreach (var (x, y) in Ring(0.6f).Concat(Ring(0.2f)))
            box.At(x, y).ShouldBe(disc.At(x, y), 1e-6);
    }

    [Fact]
    public void A_corner_radius_is_held_to_the_shorter_side()
    {
        var most = Shape(BoxType, (2, 0.5f), (3, 0.2f), (4, 0.2f));
        var more = Shape(BoxType, (2, 0.5f), (3, 0.2f), (4, 4f));

        foreach (var (x, y) in Ring(0.35f)) more.At(x, y).ShouldBe(most.At(x, y), 1e-6);
    }

    // --- the polygon -----------------------------------------------------------

    /// <summary>
    /// The radius is to the corners, one of which is straight up whatever the
    /// count — and the flats are nearer by the cosine of half a wedge, which is
    /// the number the module is built on.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(16)]
    public void A_polygon_has_a_corner_at_the_top_and_reaches_its_radius_there(int sides)
    {
        var shape = Shape(PolygonType, (2, 0.5f), (3, sides));
        var wedge = MathF.Tau / sides;

        // Every corner, starting with the one straight up.
        for (var k = 0; k < sides; k++)
            shape.At(0.5f * MathF.Sin(k * wedge), 0.5f * MathF.Cos(k * wedge)).ShouldBe(0d, 1e-6);

        // And the flat halfway between two of them, which is nearer.
        var flat = 0.5f * MathF.Cos(wedge * 0.5f);
        shape.At(flat * MathF.Sin(wedge * 0.5f), flat * MathF.Cos(wedge * 0.5f))
            .ShouldBe(0d, 1e-6);
    }

    [Fact]
    public void A_polygon_is_negative_inside_and_positive_outside()
    {
        var shape = Shape(PolygonType, (2, 0.5f), (3, 5f));

        shape.At(0f, 0f).ShouldBeLessThan(0d);
        foreach (var (x, y) in Ring(0.9f)) shape.At(x, y).ShouldBeGreaterThan(0d);
        foreach (var (x, y) in Ring(0.2f)) shape.At(x, y).ShouldBeLessThan(0d);
    }

    /// <summary>
    /// The claim the module's description makes, and the reason the fold is worth
    /// its trigonometry: the program is the same length for a triangle and for a
    /// sixteen-sided one, so a patch pays for a polygon rather than for the
    /// polygon it asked for.
    /// </summary>
    [Fact]
    public void A_polygon_costs_the_same_however_many_sides_it_has()
    {
        // Not three, which is the one count that shares a literal with the floor
        // the module holds it above — and would be a side cheaper for a reason
        // that has nothing to do with the fold.
        Program(PolygonType, 0, (3, 4f)).Ops.Length
            .ShouldBe(Program(PolygonType, 0, (3, 16f)).Ops.Length);
    }

    [Fact]
    public void A_side_count_between_two_whole_ones_is_the_lower_of_them()
    {
        var five = Shape(PolygonType, (2, 0.5f), (3, 5f));
        var between = Shape(PolygonType, (2, 0.5f), (3, 5.8f));
        var below = Shape(PolygonType, (2, 0.5f), (3, 1f));
        var three = Shape(PolygonType, (2, 0.5f), (3, 3f));

        foreach (var (x, y) in Ring(0.6f))
        {
            between.At(x, y).ShouldBe(five.At(x, y), 1e-6);
            below.At(x, y).ShouldBe(three.At(x, y), 1e-6);
        }
    }

    // --- the star --------------------------------------------------------------

    [Fact]
    public void A_star_puts_a_point_at_the_top_and_reaches_its_radius_there()
    {
        var shape = Shape(StarType, (2, 0.55f), (3, 5f));
        var wedge = MathF.Tau / 5f;

        for (var k = 0; k < 5; k++)
            shape.At(0.55f * MathF.Sin(k * wedge), 0.55f * MathF.Cos(k * wedge))
                .ShouldBe(0d, 1e-6);

        shape.At(0f, 0f).ShouldBeLessThan(0d);
    }

    /// <summary>
    /// What "exact" means, and the difference between this and the polygon beside
    /// it: a true distance field changes by one unit per unit moved.
    /// </summary>
    /// <remarks>
    /// Everywhere but the creases, which is why the sample points are placed
    /// rather than scattered. A distance field has a fold in it wherever the
    /// nearest part of the shape swaps over — down the middle of every point and
    /// every valley, which for this star is every wedge boundary — and a slope
    /// measured across one of those reads low however exact the field is, because
    /// the two sides are running away from different things. So the reading is
    /// taken halfway between two creases, where there is one nearest thing and
    /// the answer means something.
    /// </remarks>
    [Fact]
    public void A_stars_field_is_a_true_distance_all_the_way_round_it()
    {
        const int points = 5;

        var shape = Shape(StarType, (2, 0.5f), (3, points), (4, 0.7f));
        var crease = MathF.PI / points;

        foreach (var radius in new[] { 0.2f, 0.35f, 0.7f, 1.1f })
            for (var k = 0; k < points * 2; k++)
            {
                var angle = (k + 0.5f) * crease;

                Slope(shape, radius * MathF.Sin(angle), radius * MathF.Cos(angle))
                    .ShouldBe(1d, 0.02);
            }
    }

    /// <summary>
    /// And on the creases themselves, where the slope cannot be measured, what
    /// still holds: a distance field never grows faster than the distance. A
    /// field scaled by anything at all fails this everywhere.
    /// </summary>
    [Fact]
    public void A_stars_field_never_climbs_faster_than_one()
    {
        var shape = Shape(StarType, (2, 0.5f), (3, 5f), (4, 0.7f));

        foreach (var radius in new[] { 0.2f, 0.35f, 0.7f, 1.1f })
        foreach (var (x, y) in Ring(radius, 37))
            Slope(shape, x, y).ShouldBeLessThanOrEqualTo(1.001d);
    }

    /// <summary>
    /// Sharpness at nothing is the shape whose corners are its tips, which is the
    /// Polygon — two constructions sharing not one line and arriving at the same
    /// field. Inside, where the polygon's own answer is exact.
    /// </summary>
    [Fact]
    public void A_star_of_no_sharpness_is_the_polygon_through_its_points()
    {
        var star = Shape(StarType, (2, 0.5f), (3, 5f), (4, 0f));
        var polygon = Shape(PolygonType, (2, 0.5f), (3, 5f));

        foreach (var (x, y) in Ring(0.15f).Concat(Ring(0.3f)))
            star.At(x, y).ShouldBe(polygon.At(x, y), 1e-4);
    }

    [Fact]
    public void Sharpening_a_star_pulls_its_valleys_in_and_leaves_its_points_alone()
    {
        var blunt = Shape(StarType, (2, 0.5f), (3, 5f), (4, 0.2f));
        var sharp = Shape(StarType, (2, 0.5f), (3, 5f), (4, 0.9f));

        // Straight up is a tip on both, whatever the sharpness does between them.
        blunt.At(0f, 0.5f).ShouldBe(0d, 1e-6);
        sharp.At(0f, 0.5f).ShouldBe(0d, 1e-6);

        // Halfway round a wedge is a valley, and a sharper star's valley is
        // deeper — so a point that was inside the blunt one is outside this.
        var half = MathF.Tau / 10f;
        var valley = (X: 0.3f * MathF.Sin(half), Y: 0.3f * MathF.Cos(half));

        sharp.At(valley.X, valley.Y).ShouldBeGreaterThan(blunt.At(valley.X, valley.Y));
    }

    // --- the fill --------------------------------------------------------------

    [Fact]
    public void A_fill_is_one_inside_nothing_outside_and_half_on_the_edge()
    {
        var ink = Fill(0, (1, 0.02f));

        ink(-0.5f).ShouldBe(1d, 1e-9);
        ink(-0.02f).ShouldBe(1d, 1e-9);
        ink(0f).ShouldBe(0.5d, 1e-9);
        ink(0.02f).ShouldBe(0d, 1e-9);
        ink(0.5f).ShouldBe(0d, 1e-9);
    }

    /// <summary>
    /// The edge case the module is written round: at no softness the two ends of
    /// the ramp meet, and the tie has to break in favour of the inside. Written
    /// the other way — a ramp with its edges swapped rather than one subtracted
    /// from one — it breaks the other way, and every shape in the patch is a hole.
    /// </summary>
    [Fact]
    public void A_fill_with_no_softness_is_a_hard_edge_the_right_way_round()
    {
        var ink = Fill(0, (1, 0f));

        ink(-0.001f).ShouldBe(1d);
        ink(-1e-9f).ShouldBe(1d);
        ink(0.001f).ShouldBe(0d);
    }

    [Fact]
    public void An_outline_sits_on_the_edge_and_is_as_wide_as_it_says()
    {
        var line = Fill(1, (1, 0f), (2, 0.1f));

        line(0f).ShouldBe(1d);
        line(-0.04f).ShouldBe(1d);
        line(0.04f).ShouldBe(1d);
        line(-0.06f).ShouldBe(0d);
        line(0.06f).ShouldBe(0d);
    }

    /// <summary>
    /// One module rather than two, for the reason the Filter hands out three
    /// responses at once: a form and its own edge are two readings of one
    /// distance, and a patch wanting both should not have to say so twice.
    /// </summary>
    [Fact]
    public void A_fill_hands_out_the_form_and_its_edge_together()
    {
        Catalog.Require(FillType).Outputs.Select(o => o.Name).ShouldBe(["fill", "outline"]);
    }

    // --- the combine -----------------------------------------------------------

    /// <summary>
    /// At no smoothness the three outputs are exactly the arithmetic the
    /// catalogue always had, which is the claim the distance convention rests on:
    /// a shape that is a number combines with Minimum and Maximum.
    /// <para>
    /// Near enough rather than exactly, and the tolerance is the module's own
    /// floor under the seam: where the two distances are equal the blend is at
    /// its midpoint and dips by a quarter of that floor. It is a ten-thousandth
    /// of the picture at the very worst, which is a fiftieth of a pixel.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(-0.3f, 0.2f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(-0.1f, -0.7f)]
    [InlineData(0.9f, -0.2f)]
    public void A_hard_combine_is_a_minimum_a_maximum_and_a_subtraction(float a, float b)
    {
        var combined = Combined(a, b, 0f);

        combined[0].ShouldBe(MathF.Min(a, b), 1e-4);
        combined[1].ShouldBe(MathF.Max(a, b), 1e-4);
        combined[2].ShouldBe(MathF.Max(a, -b), 1e-4);
    }

    /// <summary>
    /// What the seam buys, and its bound. The blend only ever pulls a union
    /// further in — the fillet is material added where two shapes meet — and never
    /// by more than a quarter of the width it was given, which is what keeps a
    /// smoothed field near enough to a distance to fill and outline again.
    /// </summary>
    [Fact]
    public void A_smooth_union_dips_below_the_hard_one_and_not_by_much()
    {
        const float seam = 0.2f;

        for (var a = -0.5f; a <= 0.5f; a += 0.05f)
        for (var b = -0.5f; b <= 0.5f; b += 0.05f)
        {
            var combined = Combined(a, b, seam);

            combined[0].ShouldBeLessThanOrEqualTo(MathF.Min(a, b) + 1e-6f);
            combined[0].ShouldBeGreaterThanOrEqualTo(MathF.Min(a, b) - seam * 0.25f - 1e-6f);

            // And the other way about for the intersection, which is the same
            // blend read backwards.
            combined[1].ShouldBeGreaterThanOrEqualTo(MathF.Max(a, b) - 1e-6f);
            combined[1].ShouldBeLessThanOrEqualTo(MathF.Max(a, b) + seam * 0.25f + 1e-6f);
        }
    }

    [Fact]
    public void A_smooth_seam_only_shows_where_the_two_shapes_meet()
    {
        // Far apart in value, so the blend has nothing to blend and what comes
        // back is the nearer shape, unchanged.
        Combined(-0.8f, 0.6f, 0.1f)[0].ShouldBe(-0.8f, 1e-6);
        Combined(0.6f, -0.8f, 0.1f)[0].ShouldBe(-0.8f, 1e-6);
    }

    // --- whatever is patched in ------------------------------------------------

    /// <summary>
    /// Every socket here is a socket, so every number that can arrive down a wire
    /// arrives eventually — a negative radius from an oscillator, a side count
    /// swept past both ends of its own range, a size of nothing. None of them is
    /// an error and none of them may produce a NaN: one of those in a color is a
    /// black pixel on the CPU and undefined on the GPU, which is the one way the
    /// two backends could be made to disagree.
    /// </summary>
    [Fact]
    public void No_knob_anywhere_can_produce_a_number_that_is_not_one()
    {
        var wild = new[] { -1e6f, -3f, -0.4f, 0f, 1e-9f, 0.5f, 7f, 1e6f };

        foreach (var typeId in new[] { CircleType, BoxType, PolygonType, StarType, FillType })
        {
            var ports = Catalog.Require(typeId).Inputs.Count;

            for (var port = 2; port < ports; port++)
                foreach (var value in wild)
                {
                    var shape = Shape(typeId, (port, value));

                    foreach (var (x, y) in Ring(0.7f).Concat(Ring(0f)))
                        double.IsFinite(shape.At(x, y)).ShouldBeTrue(
                            $"{typeId} port {port} at {value}");
                }
        }
    }

    // --- both sinks ------------------------------------------------------------

    /// <summary>
    /// Nothing here has a memory, so unlike the Filter or the Delay these modules
    /// have no fallback to choose: the picture and the speakers run the same
    /// arithmetic and get the same number. It is what lets a Scan of a shape hear
    /// the shape.
    /// </summary>
    [Fact]
    public void A_shape_is_the_same_module_at_both_sinks()
    {
        foreach (var typeId in new[] { CircleType, BoxType, PolygonType, StarType })
        {
            var seen = Shape(typeId);
            var heard = Heard(typeId);

            foreach (var x in new[] { -0.9f, -0.3f, 0f, 0.42f, 1f })
                heard.At(x, 0f).ShouldBe(seen.At(x, 0f), 1e-9);
        }
    }

    /// <summary>
    /// The gate a video plugin has and an audio one does not: a table read is the
    /// one thing the shader cannot draw, so a module that reaches for one takes
    /// the preview back to the CPU for as long as the patch is loaded, quietly.
    /// Nothing here reaches for a table, a cell or a delay line, so all six
    /// survive to the GPU — and this is what would notice if one stopped.
    /// </summary>
    [Theory]
    [InlineData(CircleType)]
    [InlineData(BoxType)]
    [InlineData(PolygonType)]
    [InlineData(StarType)]
    [InlineData(CombineType)]
    [InlineData(FillType)]
    public void Every_module_survives_to_the_shader_backend(string typeId)
    {
        var program = Program(typeId, 0);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
        program.Ops.Any(op => op.Code is OpCode.Table or OpCode.UnitRead).ShouldBeFalse();

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    // --- the preset ------------------------------------------------------------

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == ShapesPresetName).Build(loaded.Modules);

        var types = patch.Nodes.Select(n => n.TypeId).ToList();
        types.ShouldContain(StarType);
        types.ShouldContain(CircleType);
        types.ShouldContain(CombineType);
        types.ShouldContain(FillType);

        var video = patch.CompileForVideo(loaded.Modules);
        video.Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // Nothing this plugin put in the patch asks the renderer to remember
        // anything, so the picture keeps the shader. The phases the video program
        // does carry are the two sweeps', and a phase with no state behind it is
        // the multiply it replaced — which is what the GPU lowers it to.
        video.Program.UnitCount.ShouldBe(0);
        video.Program.DelayLengths.ShouldBeEmpty();
        audio.Program.PhaseCount.ShouldBeGreaterThan(video.Program.PhaseCount);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(video.Program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// The showcase: all four forms, three Combines and one Fill, with the seam
    /// and the pulse width on one wire between them.
    /// </summary>
    [Fact]
    public void The_showcase_preset_holds_every_form_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == FormsPresetName).Build(loaded.Modules);

        var types = patch.Nodes.Select(n => n.TypeId).ToList();

        foreach (var typeId in new[] { CircleType, BoxType, PolygonType, StarType, FillType })
            types.ShouldContain(typeId);

        types.Count(t => t == CombineType).ShouldBe(3);

        var video = patch.CompileForVideo(loaded.Modules);
        var audio = patch.CompileForAudio(loaded.Modules);

        video.Issues.ShouldBeEmpty();
        audio.Issues.ShouldBeEmpty();

        video.Program.UnitCount.ShouldBe(0);
        video.Program.DelayLengths.ShouldBeEmpty();

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(video.Program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();

    }
    /// <summary>
    /// One sweep drives the three seams and the hue, which is the whole argument
    /// for the preset: the topology changing and the color changing are the same
    /// number arriving in four places. A patch where they had a sweep each would
    /// look the same at any one moment and would drift apart over a minute, which
    /// is the failure this pins.
    /// </summary>
    /// <remarks>
    /// The same sweep used to reach a Pulse as well, so the seam widening was
    /// also a duty cycle widening. That has gone with the preset's sound: the two
    /// were related by having been handed the same number and by nothing else,
    /// which is not what a patch carrying both sinks is for.
    /// </remarks>
    [Fact]
    public void The_showcase_preset_moves_the_picture_from_one_sweep()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == FormsPresetName).Build(loaded.Modules);

        // The only oscillators left are the two sweeps: the rock and the melt.
        var sweeps = patch.Nodes.Where(n => n.TypeId == "osc.sine").ToList();
        sweeps.Count.ShouldBe(2);

        patch.Nodes.ShouldNotContain(
            n => n.TypeId == "osc.pulse",
            "the preset is about forms and carries no voice");

        var melt = sweeps.Single(sweep => patch.Connections.Count(w => w.SourceNode == sweep.Id) > 1);
        var driven = patch.Connections.Where(w => w.SourceNode == melt.Id).ToList();

        // Three seams and the hue.
        driven.Count(w => Type(patch, w.TargetNode) == CombineType).ShouldBe(3);
        driven.Count(w => Type(patch, w.TargetNode) == "color.hsv").ShouldBe(1);
    }

    private static string Type(Patch patch, Guid node) =>
        patch.Nodes.Single(n => n.Id == node).TypeId;

    private const string ShapesPresetName = "Shape scan";

    private const string FormsPresetName = "Four forms";

    // --- harness ---------------------------------------------------------------

    /// <summary>One module compiled into a program, ready to be read at a position.</summary>
    private sealed class Reading(CompiledPatch program)
    {
        private readonly double[] registers = program.AllocateRegisters();

        public double At(float x, float y)
        {
            program.Evaluate(x, y, 0d, registers, default);
            return registers[program.OutputBase];
        }
    }

    private static Reading Shape(string typeId, params (int Port, float Value)[] knobs) =>
        new(Program(typeId, 0, knobs));

    /// <summary>The same module compiled into the speakers instead, read through x.</summary>
    private static Reading Heard(string typeId, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var shape = Add(patch, typeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(shape.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        return new Reading(patch.CompileForAudio(Catalog).Program);
    }

    /// <summary>One output of a Fill, as a function of the distance going into it.</summary>
    private static Func<float, double> Fill(int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        // Through Coordinates, because x is the one value an evaluation carries
        // that a knob cannot stand in for.
        var coord = Add(patch, "coord");
        var fill = Add(patch, FillType, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, fill.Id, 0);
        patch.Connect(fill.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        var reading = new Reading(patch.CompileForVideo(Catalog).Program);

        return distance => reading.At(distance, 0f);
    }

    /// <summary>
    /// All three outputs of one Combine, for two distances and a seam. One node
    /// read three times rather than three patches, because the outputs share a
    /// blend and a test that compiled them apart would not be testing the sharing.
    /// </summary>
    private static float[] Combined(float a, float b, float smoothness)
    {
        var patch = new Patch();

        var combine = Add(patch, CombineType, (0, a), (1, b), (2, smoothness));
        var channels = Add(patch, "color.rgb");
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        for (var port = 0; port < 3; port++) patch.Connect(combine.Id, port, channels.Id, port);
        patch.Connect(channels.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0d, 0d, 0d, registers, default);

        return [.. Enumerable.Range(0, 3).Select(i => (float)registers[program.OutputBase + i])];
    }

    private static CompiledPatch Program(
        string typeId, int port, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var shape = Add(patch, typeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(shape.Id, port, screen.Id, NodeCatalog.OutputColorPort);

        return patch.CompileForVideo(Catalog).Program;
    }

    private static NodeInstance Add(
        Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    /// <summary>How fast the field changes here, which is 1 for a true distance.</summary>
    private static double Slope(Reading shape, float x, float y)
    {
        const float step = 1e-3f;

        var dx = (shape.At(x + step, y) - shape.At(x - step, y)) / (2 * step);
        var dy = (shape.At(x, y + step) - shape.At(x, y - step)) / (2 * step);

        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Points evenly round a circle, offset so that none lands on an axis.</summary>
    private static IEnumerable<(float X, float Y)> Ring(float radius, int count = 16) =>
        from k in Enumerable.Range(0, count)
        let angle = MathF.Tau * (k + 0.37f) / count
        select (radius * MathF.Cos(angle), radius * MathF.Sin(angle));
}
