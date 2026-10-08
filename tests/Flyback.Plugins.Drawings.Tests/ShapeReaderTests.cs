using System.Numerics;
using System.Text;
using Flyback.Engine.Render;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Drawings.Tests;

/// <summary>
/// An SVG, an OBJ or a PNG read into one closed path at constant speed, for a Path
/// to play.
/// </summary>
public class ShapeReaderTests
{
    private static LoadedShape Read(string text, string name) => Read(Encoding.UTF8.GetBytes(text), name);

    private static LoadedShape Read(byte[] bytes, string name)
    {
        var shape = Parse(bytes, name, out var fault);

        fault.ShouldBe(ShapeFault.None);
        return shape.ShouldNotBeNull();
    }

    private static ShapeFault Fault(string text, string name)
    {
        ShapeReader.Read(Encoding.UTF8.GetBytes(text), name, out var fault).ShouldBeNull();
        return fault;
    }

    /// <summary>A file read as a Path reads it: a PNG decoded by the host, anything else parsed from its bytes.</summary>
    private static LoadedShape? Parse(byte[] bytes, string name, out ShapeFault fault)
    {
        if (!ShapeReader.IsPicture(name)) return ShapeReader.Read(bytes, name, out fault);

        var image = PngReader.Read(new MemoryStream(bytes), out _).ShouldNotBeNull("the test's picture should decode");
        return ShapeReader.Read(image, out fault);
    }

    private static Vector3[] Points(LoadedShape shape) =>
        [.. Enumerable.Range(0, shape.Points).Select(i => new Vector3(shape.X.Samples[i], shape.Y.Samples[i], shape.Z.Samples[i]))];

    /// <summary>The steps from each point to the next, round to the first again.</summary>
    private static float[] Steps(LoadedShape shape)
    {
        var points = Points(shape);

        return [.. points.Select((point, i) => Vector3.Distance(point, points[(i + 1) % points.Length]))];
    }

    private static string Svg(string inside) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">{inside}</svg>""";

    // --- the layout -----------------------------------------------------------

    [Fact]
    public void A_square_is_fitted_to_minus_one_to_one_with_y_up()
    {
        var shape = Read(Svg("""<path d="M10,10 L30,10 L30,30 L10,30 Z"/>"""), "square.svg");
        var points = Points(shape);

        points.ShouldAllBe(p => MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) > 0.999f);
        points.Min(p => p.X).ShouldBe(-1f, 1e-5f);
        points.Max(p => p.Y).ShouldBe(1f, 1e-5f);

        // SVG counts y down, so its first point, the top left, is up here.
        points[0].ShouldBe(new Vector3(-1f, 1f, 0f));
    }

    [Fact]
    public void Every_stroke_is_gone_round_at_the_same_speed()
    {
        // A long line and a short one, far apart: the short one gets fewer points, not slower ones.
        var shape = Read(Svg("""<line x1="0" y1="0" x2="80" y2="0"/><line x1="0" y1="50" x2="10" y2="50"/>"""), "lines.svg");
        var steps = Steps(shape);
        var drawn = steps.Where(step => step < 0.1f).ToList();

        drawn.Count.ShouldBeGreaterThan(ShapeLayout.Points - 10);
        drawn.Max().ShouldBeLessThan(drawn.Min() * 1.05f);
    }

    [Fact]
    public void A_jump_between_strokes_is_one_step()
    {
        var shape = Read(Svg("""<line x1="0" y1="0" x2="80" y2="0"/><line x1="0" y1="50" x2="10" y2="50"/>"""), "lines.svg");

        // Out to the second line and back from it to the start: two steps, and no more.
        Steps(shape).Count(step => step > 0.1f).ShouldBe(2);
        shape.Strokes.ShouldBe(2);
    }

    [Fact]
    public void The_table_wraps_from_its_last_point_to_its_first()
    {
        var shape = Read(Svg("""<path d="M0,0 L10,0 L10,10 Z"/>"""), "triangle.svg");

        shape.X.SampleRate.ShouldBe(shape.Points);
        shape.X.Samples.Length.ShouldBe(shape.Points + 1);
        shape.X.Samples[^1].ShouldBe(shape.X.Samples[0]);

        // A whole trip round is one second of the table, so a phase of 0 to 1 reads it.
        shape.X.Seconds.ShouldBe(1f + 1f / shape.Points, 1e-6f);
    }

    [Fact]
    public void Strokes_are_chained_nearest_first_and_turned_round_when_their_end_is_nearer()
    {
        // Drawn far, near, far: chained, the near one comes second, run backwards.
        var shape = Read(Svg("""
            <line x1="0" y1="0" x2="10" y2="0"/>
            <line x1="100" y1="0" x2="90" y2="0"/>
            <line x1="20" y1="0" x2="12" y2="0"/>
            """), "three.svg");

        var points = Points(shape);
        var jumps = Steps(shape).Select((step, i) => (step, i)).Where(s => s.step > 0.03f).ToList();

        // From the first line's end to the near one's end, then on to the far one.
        jumps.Count.ShouldBe(3);
        points[jumps[0].i + 1].X.ShouldBeLessThan(points[jumps[1].i + 1].X);
    }

    // --- SVG ------------------------------------------------------------------

    [Fact]
    public void Relative_commands_and_the_lines_an_m_implies_are_read()
    {
        var strokes = SvgStrokes.PathData("m10,10 20,0 v20 h-20 z");

        strokes.ShouldHaveSingleItem().ShouldBe([new(10, 10), new(30, 10), new(30, 30), new(10, 30), new(10, 10)]);
    }

    [Fact]
    public void Numbers_run_together_as_svg_allows()
    {
        // "-5.5.5.5" is three numbers, and "1e1" is ten.
        var strokes = SvgStrokes.PathData("M0-5.5.5.5L1e1,0");

        strokes.ShouldHaveSingleItem().ShouldBe([new(0f, -5.5f), new(0.5f, 0.5f), new(10f, 0f)]);
    }

    [Fact]
    public void A_curve_ends_where_it_says_and_bends_toward_its_control_points()
    {
        var cubic = SvgStrokes.PathData("M0,0 C0,10 10,10 10,0").ShouldHaveSingleItem();
        var quadratic = SvgStrokes.PathData("M0,0 Q5,10 10,0 T20,0").ShouldHaveSingleItem();

        cubic[^1].ShouldBe(new Vector2(10, 0));
        cubic.Max(p => p.Y).ShouldBe(7.5f, 0.1f);

        // The smooth continuation mirrors the control point, so the second hump hangs below.
        quadratic[^1].ShouldBe(new Vector2(20, 0));
        quadratic.Max(p => p.Y).ShouldBe(5f, 0.1f);
        quadratic.Min(p => p.Y).ShouldBe(-5f, 0.1f);
    }

    [Fact]
    public void An_arc_goes_the_way_its_flags_say()
    {
        // Half a circle of radius 5 from (0,0) to (10,0): sweep 1 bulges toward negative y, sweep 0 toward positive.
        var one = SvgStrokes.PathData("M0,0 A5,5 0 0 1 10,0").ShouldHaveSingleItem();
        var zero = SvgStrokes.PathData("M0,0 A5,5 0 0 0 10,0").ShouldHaveSingleItem();

        one[^1].X.ShouldBe(10f, 1e-4f);
        one.Min(p => p.Y).ShouldBe(-5f, 0.05f);
        zero.Max(p => p.Y).ShouldBe(5f, 0.05f);
        one.ShouldAllBe(p => MathF.Abs(Vector2.Distance(p, new Vector2(5, 0)) - 5f) < 1e-3f);
    }

    [Fact]
    public void An_arc_too_small_to_reach_is_grown_until_it_does()
    {
        var arc = SvgStrokes.PathData("M0,0 A1,1 0 0 1 10,0").ShouldHaveSingleItem();

        arc[^1].X.ShouldBe(10f, 1e-4f);
        arc.Min(p => p.Y).ShouldBe(-5f, 0.05f);
    }

    [Fact]
    public void Transforms_apply_right_to_left_and_nest()
    {
        var matrix = SvgStrokes.Transform("translate(10 0) scale(2)");

        Vector2.Transform(new Vector2(1, 1), matrix).ShouldBe(new Vector2(12, 2));

        // The element's own scale first, then its group's move, then the outer group's scale; y comes back up.
        var bytes = Encoding.UTF8.GetBytes(Svg("""
            <g transform="scale(3)"><g transform="translate(10,0)"><line x1="0" y1="1" x2="2" y2="1" transform="scale(2)"/></g></g>
            """));

        SvgStrokes.Read(bytes, int.MaxValue, out _).ShouldNotBeNull().ShouldHaveSingleItem()
            .ShouldBe([new Vector3(30, -6, 0), new Vector3(42, -6, 0)]);
    }

    [Fact]
    public void Shapes_are_drawn_round_their_edges()
    {
        var shape = Read(Svg("""<rect x="0" y="0" width="20" height="10"/><circle cx="50" cy="50" r="10"/><ellipse cx="80" cy="80" rx="5" ry="2"/>"""), "shapes.svg");

        shape.Strokes.ShouldBe(3);
    }

    [Fact]
    public void Nothing_under_defs_or_hidden_is_drawn()
    {
        var shape = Read(Svg("""
            <defs><path d="M0,0 L100,100"/></defs>
            <path d="M0,0 L100,0" display="none"/>
            <line x1="0" y1="0" x2="10" y2="0"/>
            """), "hidden.svg");

        shape.Strokes.ShouldBe(1);
    }

    [Fact]
    public void An_element_after_one_skipped_is_still_drawn()
    {
        // Skipping a subtree leaves the reader on the next element, which must not be passed over.
        var shape = Read(Svg("""<defs><path d="M0,0 L1,1"/></defs><line x1="0" y1="0" x2="10" y2="0"/>"""), "after.svg");

        shape.Strokes.ShouldBe(1);
    }

    [Fact]
    public void An_entity_is_never_fetched_or_expanded()
    {
        var text = """
            <?xml version="1.0"?>
            <!DOCTYPE svg [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <svg xmlns="http://www.w3.org/2000/svg"><line x1="0" y1="0" x2="10" y2="0"/><title>&secret;</title></svg>
            """;

        // The line before the entity is read; the entity stops the reading rather than being looked up.
        Read(text, "hostile.svg").Strokes.ShouldBe(1);
    }

    [Fact]
    public void A_file_cut_short_gives_what_came_before_the_cut()
    {
        Read("""<svg><line x1="0" y1="0" x2="10" y2="0"/><line x1="0" y1""", "cut.svg").Strokes.ShouldBe(1);
    }

    [Fact]
    public void What_is_not_an_svg_is_refused_and_an_svg_with_nothing_drawn_is_empty()
    {
        Fault("hello", "x.svg").ShouldBe(ShapeFault.NotShape);
        Fault("<html><body/></html>", "x.svg").ShouldBe(ShapeFault.NotShape);
        Fault(Svg(""), "x.svg").ShouldBe(ShapeFault.Empty);
    }

    // --- OBJ ------------------------------------------------------------------

    private const string Cube = """
        v -1 -1 -1
        v  1 -1 -1
        v  1  1 -1
        v -1  1 -1
        v -1 -1  1
        v  1 -1  1
        v  1  1  1
        v -1  1  1
        f 1 2 3 4
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;

    [Fact]
    public void A_cube_is_its_twelve_edges_each_drawn_once()
    {
        var shape = Read(Cube, "cube.obj");

        // Fitted to the unit sphere, so an edge is 2/√3 long; the jumps between trails are left out.
        var edge = 2f / MathF.Sqrt(3f);
        var drawn = Steps(shape).Where(step => step < 0.05f).Sum();

        drawn.ShouldBe(12f * edge, 0.05f);

        // Every corner meets three edges, so the walk needs four trails at least.
        shape.Strokes.ShouldBeInRange(4, 6);
    }

    [Fact]
    public void A_model_is_fitted_inside_the_unit_sphere_so_it_stays_in_bounds_as_it_turns()
    {
        var points = Points(Read(Cube, "cube.obj"));

        points.Max(p => p.Length()).ShouldBe(1f, 1e-4f);
        points.Max(p => p.Z).ShouldBeGreaterThan(0.5f);
    }

    [Fact]
    public void Faces_with_texture_and_normal_indices_and_indices_from_the_end_are_read()
    {
        var shape = Read("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f -3/1/1 -2/1/1 -1/1/1
            f 1//1 2//1 9//1
            """, "triangle.obj");

        shape.Strokes.ShouldBe(1);
    }

    [Fact]
    public void An_obj_with_no_faces_is_empty_and_text_with_no_vertices_is_refused()
    {
        Fault("v 0 0 0\nv 1 1 1\n", "points.obj").ShouldBe(ShapeFault.Empty);
        Fault("hello there", "x.obj").ShouldBe(ShapeFault.NotShape);
    }

    // --- PNG ------------------------------------------------------------------

    /// <summary>A white picture with black where <paramref name="ink"/> says.</summary>
    private static byte[] Png(int width, int height, Func<int, int, bool> ink)
    {
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = ink(x, y) ? (byte)0 : (byte)255;
                var at = (y * width + x) * 4;

                (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = (value, value, value, 255);
            }
        }

        using var stream = new MemoryStream();
        PngWriter.WriteBgra(stream, bgra, width, height, width * 4);
        return stream.ToArray();
    }

    [Fact]
    public void A_filled_square_is_traced_round_its_edge()
    {
        var shape = Read(Png(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30), "square.png");

        shape.Strokes.ShouldBe(1);
        Points(shape).ShouldAllBe(p => MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) > 0.97f);
    }

    [Fact]
    public void A_line_is_traced_round_both_its_edges_and_a_speck_is_dropped()
    {
        var shape = Read(Png(60, 40, (x, y) => (y is >= 18 and < 22 && x is >= 5 and < 55) || (x == 2 && y == 2)), "line.png");

        shape.Strokes.ShouldBe(1);
    }

    [Fact]
    public void A_ring_is_two_outlines()
    {
        var shape = Read(Png(60, 60, (x, y) =>
        {
            var r = MathF.Sqrt((x - 30) * (x - 30) + (y - 30) * (y - 30));
            return r is > 15 and < 22;
        }), "ring.png");

        shape.Strokes.ShouldBe(2);
    }

    [Fact]
    public void A_blank_picture_is_empty()
    {
        Parse(Png(10, 10, (_, _) => false), "blank.png", out var blank).ShouldBeNull();
        blank.ShouldBe(ShapeFault.Empty);
    }

    // --- the door -------------------------------------------------------------

    [Fact]
    public void A_kind_of_file_it_does_not_read_is_refused_by_name()
    {
        Fault("anything", "song.mid").ShouldBe(ShapeFault.Unsupported);
        ShapeReader.Explain(ShapeFault.Unsupported).ShouldContain(".svg");
    }

    [Fact]
    public void A_file_past_the_size_cap_is_refused_before_it_is_read()
    {
        ShapeReader.Read(new byte[ShapeReader.MostBytes + 1], "huge.svg", out var fault).ShouldBeNull();

        fault.ShouldBe(ShapeFault.TooBig);
    }

    [Fact]
    public void A_model_past_the_point_cap_is_refused()
    {
        var many = new StringBuilder();

        for (var i = 0; i <= ShapeReader.MostPoints; i++) many.Append("v 0 0 0\n");

        Fault(many.ToString(), "many.obj").ShouldBe(ShapeFault.TooBig);
    }
}
