using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Text, loaded off disk and read the way the renderer reads it: the distance to
/// the letters, one evaluation a point.
/// </summary>
/// <remarks>
/// Positions are worked in font pixels and turned into the picture's units by
/// <see cref="Pixel"/>: at the default size a capital is seven of them tall, the
/// middle of the capitals is at nought, and the widest line's ink is centred
/// across. Row r of a glyph is centred at 3 - r, and column c of letter k of an
/// n-letter line at 6k + c - (6n - 1) / 2 + 0.5.
/// </remarks>
public class TextTests
{
    private const string TextType = "flyback.picture.text";
    private const string CaptionsName = "Captions";

    private const int LinePort = 3;
    private const int RevealPort = 4;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    /// <summary>One font pixel at the default size, in the picture's units.</summary>
    private const float Pixel = 0.2f / 7f;

    // --- the catalogue ---------------------------------------------------------

    [Fact]
    public void Text_is_a_form_that_sits_in_the_middle_of_the_picture()
    {
        var def = Catalog.Require(TextType);

        def.Category.ShouldBe(ModuleCategories.Forms);
        Catalog.Normalled(def.Inputs[0]).ShouldBe("Coordinates x");
        Catalog.Normalled(def.Inputs[1]).ShouldBe("Coordinates y");
        def.Inputs[LinePort].Name.ShouldBe("line");
        def.Inputs[RevealPort].Name.ShouldBe("reveal");
    }

    // --- the distance ----------------------------------------------------------

    /// <summary>
    /// A fresh Text says hello, and the edge of its H is exactly where the H's
    /// left stem starts, with the distance outside it the distance to the stem.
    /// </summary>
    /// <remarks>
    /// Outside and on the edge rather than inside: a stroke is one pixel wide, so
    /// the distance within it is a ridge half a pixel high, and a texture read
    /// between texels a quarter of a pixel apart rounds the top of it off. The
    /// edge is what a Fill draws, and it is exact.
    /// </remarks>
    [Fact]
    public void A_fresh_text_says_hello_with_its_edge_where_the_ink_is()
    {
        var hello = Shape(null);

        hello.At(-14f * Pixel, 0f).ShouldBeLessThan(0d);
        hello.At(-14.5f * Pixel, 2f * Pixel).ShouldBe(0d, 0.01 * Pixel);
        hello.At(-15.5f * Pixel, 2f * Pixel).ShouldBe(1d * Pixel, 0.01 * Pixel);
        hello.At(-17f * Pixel, 2f * Pixel).ShouldBe(2.5d * Pixel, 0.01 * Pixel);
    }

    /// <summary>
    /// Past the atlas the box around the ink takes over, so a point across the
    /// frame is as far away as it looks rather than reading the black outside
    /// the picture as ink.
    /// </summary>
    [Fact]
    public void Far_from_the_letters_the_distance_is_to_the_box_they_sit_in()
    {
        var hello = Shape(null);

        hello.At(2f, 0f).ShouldBe(2f - 14.5f * Pixel, 1e-4);
        hello.At(0f, -0.9f).ShouldBeGreaterThan(0.5);
    }

    /// <summary>A true distance changes at one unit per unit, near an edge at least.</summary>
    [Fact]
    public void Near_an_edge_the_field_is_a_true_distance()
    {
        var hello = Shape(null);

        foreach (var (x, y) in new[] { (-15.5f, 0f), (-14.2f, 0.4f), (-10f, -4.5f) })
            Slope(hello, x * Pixel, y * Pixel).ShouldBe(1d, 0.05);
    }

    [Fact]
    public void Nothing_to_say_is_nowhere_near_and_reads_no_picture()
    {
        var program = Program(string.Empty);

        new Reading(program).At(0f, 0f).ShouldBe(2d);
        program.Pictures.ShouldBeEmpty();
    }

    /// <summary>
    /// Something this font has no letter for is a box, so what was typed is seen
    /// to be there. The box's left side is its first column.
    /// </summary>
    [Fact]
    public void A_letter_the_font_does_not_have_is_drawn_as_a_box()
    {
        Shape("é").At(-2f * Pixel, 0f).ShouldBeLessThan(0d);
        Shape(" ").At(-2f * Pixel, 0f).ShouldBeGreaterThan(0d);
    }

    // --- choosing a line -------------------------------------------------------

    /// <summary>
    /// Three one-letter lines, read at three points each inked by one of them
    /// alone: above the middle for the bar, at the left for the dash, and low
    /// for the dot.
    /// </summary>
    [Theory]
    [InlineData(0f, "bar")]
    [InlineData(1f, "dash")]
    [InlineData(1.9f, "dash")]
    [InlineData(4f, "dash")]
    [InlineData(2f, "dot")]
    [InlineData(-1f, "dot")]
    public void Line_chooses_which_is_shown_and_wraps_round(float line, string shown)
    {
        var text = Shape("|\n-\n.", (LinePort, line));

        (text.At(0f, 2f * Pixel) < 0).ShouldBe(shown == "bar");
        (text.At(-2f * Pixel, 0f) < 0).ShouldBe(shown == "dash");
        (text.At(-1f * Pixel, -2f * Pixel) < 0).ShouldBe(shown == "dot");
    }

    /// <summary>
    /// A shorter line is centred under a longer one, so a page of captions reads
    /// as a column rather than as a ragged left edge.
    /// </summary>
    [Fact]
    public void A_short_line_is_centred_in_the_widest()
    {
        var text = Shape("-\n---", (LinePort, 0f));

        // The one dash sits where the middle letter of three would.
        text.At(0f, 0f).ShouldBeLessThan(0d);
        text.At(-6f * Pixel, 0f).ShouldBeGreaterThan(0d);
    }

    // --- typing it out ---------------------------------------------------------

    [Theory]
    [InlineData(0f, false, false)]
    [InlineData(0.49f, false, false)]
    [InlineData(0.5f, true, false)]
    [InlineData(0.99f, true, false)]
    [InlineData(1f, true, true)]
    public void Reveal_uncovers_the_line_a_letter_at_a_time(float reveal, bool first, bool second)
    {
        var text = Shape("--", (RevealPort, reveal));

        (text.At(-3f * Pixel, 0f) < 0).ShouldBe(first);
        (text.At(3f * Pixel, 0f) < 0).ShouldBe(second);
    }

    // --- the font --------------------------------------------------------------

    /// <summary>
    /// The font is a choice on the panel, which is a list to pick from, and a
    /// fresh Text is in Pixel.
    /// </summary>
    [Fact]
    public void The_font_is_picked_from_a_list_and_starts_as_pixel()
    {
        var font = Catalog.Require(TextType).Extras.Single().Fields
            .OfType<ExtraField.Choice>().Single(field => field.Key == "font");

        font.Options.Select(option => option.Name).ShouldBe(["Pixel", "Tiny"]);
        font.Fallback.ShouldBe("pixel");
    }

    /// <summary>
    /// 'size' is the height of a capital whichever font draws it, so changing the
    /// font changes the letters and not how big they are. An I's top bar is at
    /// half a capital above the middle in both.
    /// </summary>
    [Theory]
    [InlineData("pixel")]
    [InlineData("tiny")]
    public void Size_is_the_height_of_a_capital_in_either_font(string font)
    {
        var text = new Reading(Program("I", font));

        text.At(0f, 0.095f).ShouldBeLessThan(0d);
        text.At(0f, 0.105f).ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// Tiny has one case: a small letter is its capital, read at the same
    /// points to the same distance.
    /// </summary>
    [Fact]
    public void Tiny_draws_a_small_letter_as_its_capital()
    {
        var small = new Reading(Program("a", "tiny"));
        var capital = new Reading(Program("A", "tiny"));

        foreach (var (x, y) in new[] { (-0.04f, 0.08f), (0f, 0.03f), (0.03f, -0.07f), (0f, -0.02f) })
            small.At(x, y).ShouldBe(capital.At(x, y), 1e-9);

        small.At(-0.04f, 0.08f).ShouldBeLessThan(0d);
    }

    [Fact]
    public void The_same_lines_in_another_font_are_another_picture()
    {
        var pixel = Program("Hello", "pixel").Pictures.ShouldHaveSingleItem();
        var tiny = Program("Hello", "tiny").Pictures.ShouldHaveSingleItem();

        tiny.ShouldNotBeSameAs(pixel);
        tiny.Height.ShouldBeLessThan(pixel.Height);
    }

    /// <summary>
    /// A font this build does not have is drawn in Pixel and said so, and the
    /// choice is left as it was, so the patch still names it when saved again.
    /// </summary>
    [Fact]
    public void A_font_this_build_lacks_is_drawn_in_pixel_and_said_so()
    {
        var patch = Patched("Hello", "gothic");

        var issue = patch.CompileForVideo(Catalog).Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Message.ShouldContain("'gothic'");

        new Reading(patch.CompileForVideo(Catalog).Program).At(-14f * Pixel, 0f)
            .ShouldBe(Shape(null).At(-14f * Pixel, 0f), 1e-9);

        patch.Nodes.Single(n => n.TypeId == TextType).StateOf("text")!["font"]!
            .GetValue<string>().ShouldBe("gothic");
    }

    // --- what the renderer is handed -------------------------------------------

    /// <summary>
    /// The renderer keeps a texture for as long as it is handed the same picture,
    /// so a knob turned beside a Text must hand it the same one again.
    /// </summary>
    [Fact]
    public void Recompiling_the_same_lines_hands_over_the_same_picture()
    {
        var first = Program("same lines", (2, 0.2f)).Pictures.ShouldHaveSingleItem();
        var again = Program("same lines", (2, 0.3f)).Pictures.ShouldHaveSingleItem();
        var other = Program("other lines", (2, 0.2f)).Pictures.ShouldHaveSingleItem();

        again.ShouldBeSameAs(first);
        other.ShouldNotBeSameAs(first);
    }

    /// <summary>
    /// A shape heard is the same shape: the speakers' program bakes the same
    /// atlas and reads it the same way, so a loop swept through a word plays its
    /// letters.
    /// </summary>
    [Fact]
    public void Text_is_heard_as_the_same_field_it_draws()
    {
        var seen = Shape(null);
        var heard = Heard();

        foreach (var x in new[] { -14f, -15f, -3f, 0f, 9.5f })
            heard.At(x * Pixel, 0f).ShouldBe(seen.At(x * Pixel, 0f), 1e-9);
    }

    [Fact]
    public void Text_keeps_the_shader_and_remembers_nothing()
    {
        var program = Program("Hi");

        program.UnitCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldContain("pic0(");
    }

    [Fact]
    public void More_than_an_atlas_holds_is_shown_in_part_and_said_so()
    {
        var patch = Patched(string.Join('\n', Enumerable.Range(0, 70).Select(n => $"line {n}")));

        var issue = patch.CompileForVideo(Catalog).Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Message.ShouldContain("first 64 lines");
    }

    // --- the preset ------------------------------------------------------------

    /// <summary>
    /// The clock's count is the line and what is left of it is the typing, so the
    /// picture starts empty, has a whole line up by the end of its two seconds,
    /// and shows a different one in the next two.
    /// </summary>
    [Fact]
    public void Captions_types_each_line_out_and_moves_on()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == CaptionsName).Build(loaded.Modules);

        patch.Nodes.ShouldContain(n => n.TypeId == TextType);
        patch.Reaches().ShouldBe((true, false));

        var video = patch.CompileForVideo(loaded.Modules);
        video.Issues.ShouldBeEmpty();

        var start = Lit(video.Program, 0.01);
        var first = Lit(video.Program, 1.9);
        var second = Lit(video.Program, 3.9);

        start.ShouldBeEmpty();
        first.ShouldNotBeEmpty();
        second.ShouldNotBeEmpty();
        second.SetEquals(first).ShouldBeFalse();
    }

    // --- harness ---------------------------------------------------------------

    private sealed class Reading(CompiledPatch program)
    {
        private readonly double[] registers = program.AllocateRegisters();

        public double At(float x, float y)
        {
            program.Evaluate(x, y, 0d, registers, default);
            return registers[program.OutputBase];
        }
    }

    /// <summary>A Text saying <paramref name="lines"/>, or what a fresh one says where that is null.</summary>
    private static Reading Shape(string? lines, params (int Port, float Value)[] knobs) =>
        new(Program(lines, knobs));

    private static Reading Heard()
    {
        var patch = new Patch();

        var text = Add(patch, TextType);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        patch.Connect(text.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        return new Reading(patch.CompileForAudio(Catalog).Program);
    }

    private static CompiledPatch Program(string? lines, params (int Port, float Value)[] knobs) =>
        Patched(lines, knobs).CompileForVideo(Catalog).Program;

    private static CompiledPatch Program(string lines, string font) =>
        Patched(lines, font).CompileForVideo(Catalog).Program;

    private static Patch Patched(string lines, string font)
    {
        var patch = Patched(lines);

        patch.Nodes.Single(n => n.TypeId == TextType).StateOf("text")!["font"] = font;

        return patch;
    }

    private static Patch Patched(string? lines, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var text = Add(patch, TextType, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        if (lines is not null) text.SetState("text", new JsonObject { ["lines"] = lines });

        patch.Connect(text.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        return patch;
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static double Slope(Reading shape, float x, float y)
    {
        const float step = 1e-3f;

        var dx = (shape.At(x + step, y) - shape.At(x - step, y)) / (2 * step);
        var dy = (shape.At(x, y + step) - shape.At(x, y - step)) / (2 * step);

        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Which points of a coarse grid over the frame are lit at a moment.</summary>
    private static HashSet<(int, int)> Lit(CompiledPatch program, double t)
    {
        var registers = program.AllocateRegisters();
        var lit = new HashSet<(int, int)>();

        for (var i = 0; i < 320; i++)
            for (var j = 0; j < 90; j++)
            {
                var x = -16d / 9d + (i + 0.5) * (32d / 9d) / 320;
                var y = -1d + (j + 0.5) * 2d / 90;

                program.Evaluate(x, y, t, registers, default);

                if (registers[program.OutputBase + 1] > 0.5) lit.Add((i, j));
            }

        return lit;
    }
}
