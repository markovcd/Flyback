using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The four color modules: one to choose a color, one to read one back, and
/// two to change one after the fact.
/// </summary>
/// <remarks>
/// Every one of these is a pure function of the numbers going into it, so most
/// of what is here is a table of colors and the numbers that come back. The one
/// exception is the way To HSV is checked: it is the inverse of a module that
/// already existed, and the honest test of an inverse is the round trip, so that
/// is what it gets.
/// </remarks>
public class ColorTests
{
    private const string Palette = "flyback.picture.palette";
    private const string ToHsv = "flyback.picture.hsv";
    private const string Grade = "flyback.picture.grade";
    private const string Posterise = "flyback.picture.posterise";

    private const string Hsv = "color.hsv";
    private const string Rgb = "color.rgb";

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    // --- the catalogue ---------------------------------------------------------

    [Fact]
    public void The_plugin_offers_all_four_from_one_assembly()
    {
        foreach (var typeId in new[] { Palette, ToHsv, Grade, Posterise })
        {
            Catalog.Get(typeId).ShouldNotBeNull();
            Catalog.ProviderOf(typeId).ShouldBe(Catalog.ProviderOf(Palette));

            // The engine's own color section rather than one of their own: they
            // are the same kind of thing as what is already there.
            Catalog.Require(typeId).Category.ShouldBe(Catalog.Require(Rgb).Category);
        }
    }

    // --- the palette -----------------------------------------------------------

    /// <summary>
    /// The three channels are one cosine read at three phases, so the arithmetic
    /// is exact and can be checked rather than described. At the top of the wave
    /// red is at the peak and the other two are a third of a cycle either side of
    /// it, which is a quarter of the way down.
    /// </summary>
    [Fact]
    public void A_palette_is_one_cosine_read_at_three_phases()
    {
        var swatch = Color(Palette);

        swatch(0f).ShouldBe((1f, 0.25f, 0.25f), 1e-5f);
        swatch(0.5f).ShouldBe((0f, 0.75f, 0.75f), 1e-5f);
    }

    /// <summary>
    /// The knob that changes the family rather than the position in it. At
    /// nothing the three channels are the same wave with no phase between them,
    /// which is a grey — and every value of it is a palette somebody could have
    /// meant, which is the property worth having.
    /// </summary>
    [Fact]
    public void No_spread_is_a_grey_ramp_and_a_third_is_the_rainbow()
    {
        var grey = Color(Palette, (2, 0f));
        var rainbow = Color(Palette);

        foreach (var t in Along())
        {
            var (r, g, b) = grey(t);

            g.ShouldBe(r, 1e-6f);
            b.ShouldBe(r, 1e-6f);
        }

        // A third apart pulls the channels as far from each other as they go, so
        // somewhere along it the picture is thoroughly colored. Not to the
        // corners of the cube, though, and that is the palette rather than a
        // shortfall: three cosines a third apart are never at one and nought
        // together, which is exactly why what it passes through are neighbours.
        Along().Max(t => Chroma(rainbow(t))).ShouldBeInRange(0.7f, 0.87f);
    }

    [Fact]
    public void The_default_palette_fills_nought_to_one_and_leaves_neither_end()
    {
        var swatch = Color(Palette);

        var seen = Along(64).Select(swatch).ToList();

        seen.ShouldAllBe(c => c.R >= -1e-6f && c.R <= 1.000001f);
        seen.ShouldContain(c => c.R > 0.999f);
        seen.ShouldContain(c => c.R < 0.001f);
    }

    [Fact]
    public void Cycles_repeats_the_palette_across_the_input()
    {
        var twice = Color(Palette, (1, 2f));

        foreach (var t in Along()) twice(t + 0.5f).ShouldBe(twice(t), 1e-5f);
    }

    /// <summary>
    /// Contrast is how far either side of the middle the palette reaches, so at
    /// nothing there is no palette left — one flat color, whatever is asked of
    /// it. Which is the sane thing for a knob to do at its bottom rather than a
    /// division by nothing or a black frame.
    /// </summary>
    [Fact]
    public void No_contrast_is_one_flat_color()
    {
        var flat = Color(Palette, (3, 0.3f), (4, 0f));

        foreach (var t in Along()) flat(t).ShouldBe((0.3f, 0.3f, 0.3f), 1e-6f);
    }

    // --- to HSV ----------------------------------------------------------------

    /// <summary>
    /// The honest test of an inverse. Every hue, saturation and value through the
    /// module that already existed and back out through the one that did not,
    /// and the numbers have to be the ones that went in.
    /// </summary>
    [Fact]
    public void A_color_built_from_hsv_reads_back_as_the_hsv_it_was_built_from()
    {
        var round = RoundTrip();

        for (var h = 0f; h < 1f; h += 1f / 12f)
        for (var s = 0.2f; s <= 1f; s += 0.4f)
        for (var v = 0.3f; v <= 1f; v += 0.35f)
        {
            var (hue, saturation, value) = round(h, s, v);

            // The hue of a color is a circle, so a hue that came back at the
            // other end of it is the same answer.
            var apart = MathF.Abs(hue - h);

            MathF.Min(apart, 1f - apart).ShouldBeLessThan(1e-4f);
            saturation.ShouldBe(s, 1e-4f);
            value.ShouldBe(v, 1e-4f);
        }
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, 1f, 1f)]            // red is where the wheel starts
    [InlineData(0f, 1f, 0f, 1f / 3f, 1f, 1f)]
    [InlineData(0f, 0f, 1f, 2f / 3f, 1f, 1f)]
    [InlineData(0f, 1f, 1f, 0.5f, 1f, 1f)]          // cyan, opposite red
    [InlineData(1f, 0f, 0.5f, 0.9166667f, 1f, 1f)]  // between magenta and red, where the maths goes negative
    [InlineData(0.5f, 0.5f, 0.5f, 0f, 0f, 0.5f)]    // a grey has no hue to report
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]            // and black divides by nothing twice
    public void Every_corner_of_the_wheel_reads_as_it_should(
        float r, float g, float b, float hue, float saturation, float value)
    {
        Taken(r, g, b).ShouldBe((hue, saturation, value), 1e-5f);
    }

    /// <summary>
    /// Red's own expression is negative below the axis, and a hue is not. The
    /// wrap is a Fract rather than a comparison, which both backends already
    /// agree about for a negative number.
    /// </summary>
    [Fact]
    public void A_hue_never_comes_back_negative()
    {
        for (var b = 0f; b <= 1f; b += 0.05f)
        {
            var (hue, _, _) = Taken(1f, 0f, b);

            hue.ShouldBeInRange(0f, 1f);
        }
    }

    // --- the grade -------------------------------------------------------------

    [Fact]
    public void A_grade_at_its_defaults_is_a_wire()
    {
        var graded = Through(Grade);

        foreach (var (r, g, b) in Swatches()) graded(r, g, b).ShouldBe((r, g, b), 1e-5f);
    }

    /// <summary>
    /// Saturation mixes towards the picture's own brightness, which is the
    /// weighted one the eye uses rather than the average of the channels — so a
    /// pure green greys to something bright and a pure blue to something dark.
    /// </summary>
    [Fact]
    public void No_saturation_is_a_proper_greyscale()
    {
        var grey = Through(Grade, (1, 0f));

        grey(0f, 1f, 0f).ShouldBe((0.7152f, 0.7152f, 0.7152f), 1e-4f);
        grey(0f, 0f, 1f).ShouldBe((0.0722f, 0.0722f, 0.0722f), 1e-4f);

        foreach (var (r, g, b) in Swatches())
        {
            var (red, green, blue) = grey(r, g, b);

            green.ShouldBe(red, 1e-5f);
            blue.ShouldBe(red, 1e-5f);
        }
    }

    /// <summary>
    /// Contrast about the middle rather than about black, which is the whole
    /// difference between it and the Gain that was already here: the middle grey
    /// is the one color no amount of it moves.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void Contrast_leaves_the_middle_where_it_is(float contrast)
    {
        Through(Grade, (2, contrast))(0.5f, 0.5f, 0.5f).ShouldBe((0.5f, 0.5f, 0.5f), 1e-5f);
    }

    [Fact]
    public void Gamma_deepens_what_is_under_the_middle_and_leaves_the_ends_alone()
    {
        var deep = Through(Grade, (3, 2f));

        deep(0.5f, 0.5f, 0.5f).ShouldBe((0.25f, 0.25f, 0.25f), 1e-5f);
        deep(1f, 1f, 1f).ShouldBe((1f, 1f, 1f), 1e-5f);
        deep(0f, 0f, 0f).ShouldBe((0f, 0f, 0f), 1e-5f);
    }

    /// <summary>
    /// Contrast can push a channel below nothing, and a negative number raised to
    /// a fractional power has no answer at all — which the engine's guard turns
    /// into black, a hole in the picture rather than a dark part of it. Held
    /// above nought before the power, so what comes out is the dark part.
    /// </summary>
    [Fact]
    public void A_channel_driven_under_nothing_comes_back_as_nothing()
    {
        var hard = Through(Grade, (2, 4f), (3, 0.5f));

        var (r, g, b) = hard(0.1f, 0.2f, 0.3f);

        float.IsFinite(r).ShouldBeTrue();
        r.ShouldBe(0f);
        g.ShouldBe(0f);
        float.IsFinite(b).ShouldBeTrue();
    }

    // --- the posterise ---------------------------------------------------------

    [Fact]
    public void Two_levels_is_every_channel_off_or_on()
    {
        var flat = Through(Posterise, (1, 2f));

        foreach (var (r, g, b) in Swatches())
        foreach (var channel in Channels(flat(r, g, b)))
            channel.ShouldBeOneOf(0f, 1f);
    }

    /// <summary>
    /// The levels are placed on the ends rather than between them, which is the
    /// part that is easy to get wrong: the obvious arithmetic never reaches
    /// white, because the top step begins at one and there is nothing above it.
    /// </summary>
    [Fact]
    public void Black_stays_black_and_white_stays_white()
    {
        foreach (var levels in new[] { 2f, 3f, 4f, 7f, 32f })
        {
            var banded = Through(Posterise, (1, levels));

            banded(0f, 0f, 0f).ShouldBe((0f, 0f, 0f), 1e-6f);
            banded(1f, 1f, 1f).ShouldBe((1f, 1f, 1f), 1e-6f);
        }
    }

    /// <summary>
    /// And the property that says the same thing without naming a number: a
    /// picture already held to the levels is not moved by being held to them
    /// again. An off-by-one at either end fails this everywhere.
    /// </summary>
    [Fact]
    public void Posterising_a_posterised_picture_changes_nothing()
    {
        var once = Through(Posterise, (1, 5f));
        var twice = Through(Posterise, (1, 5f));

        foreach (var (r, g, b) in Swatches())
        {
            var (pr, pg, pb) = once(r, g, b);

            twice(pr, pg, pb).ShouldBe((pr, pg, pb), 1e-6f);
        }
    }

    [Fact]
    public void A_level_count_between_two_whole_ones_is_the_lower_of_them()
    {
        var four = Through(Posterise, (1, 4f));
        var between = Through(Posterise, (1, 4.9f));
        var below = Through(Posterise, (1, 0f));
        var two = Through(Posterise, (1, 2f));

        foreach (var (r, g, b) in Swatches())
        {
            between(r, g, b).ShouldBe(four(r, g, b), 1e-6f);
            below(r, g, b).ShouldBe(two(r, g, b), 1e-6f);
        }
    }

    // --- both sinks and the shader ---------------------------------------------

    [Theory]
    [InlineData(Palette)]
    [InlineData(ToHsv)]
    [InlineData(Grade)]
    [InlineData(Posterise)]
    public void Every_module_survives_to_the_shader(string typeId)
    {
        var program = Lit(typeId);

        program.UnitCount.ShouldBe(0);
        program.PhaseCount.ShouldBe(0);
        program.DelayLengths.ShouldBeEmpty();
        program.Tables.Count.ShouldBe(0);

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    // --- the preset ------------------------------------------------------------

    /// <summary>
    /// The preset builds, compiles, and is one gesture: a single sweep opening
    /// the palette and flattening the posterise together.
    /// </summary>
    /// <remarks>
    /// The ear cannot read the field at all here, x and y being the pixel's own
    /// position and the speakers having no pixel, so sharing this knob with a
    /// sound would not be the picture being heard. It drives only the palette's
    /// spread and the posterise's levels.
    /// </remarks>
    [Fact]
    public void The_preset_builds_and_is_one_gesture()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Spectrum").Build(loaded.Modules);

        var types = patch.Nodes.Select(n => n.TypeId).ToList();
        types.ShouldContain(Palette);
        types.ShouldContain(Grade);
        types.ShouldContain(Posterise);

        var video = patch.CompileForVideo(loaded.Modules);
        var audio = patch.CompileForAudio(loaded.Modules);

        video.Issues.ShouldBeEmpty();
        audio.Issues.ShouldBeEmpty();

        // The one sweep drives the palette's spread and the posterise's levels.
        var sweep = patch.Nodes.Single(n =>
            n.TypeId == "osc.sine" && patch.Connections.Count(w => w.SourceNode == n.Id) > 1);

        patch.Connections.Count(w => w.SourceNode == sweep.Id).ShouldBe(2);
    }

    // --- harness ---------------------------------------------------------------

    /// <summary>A module read as a color, as a function of one scalar into its first port.</summary>
    private static Func<float, (float R, float G, float B)> Color(
        string typeId, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        // Through Coordinates, because x is the one value an evaluation carries
        // that a knob cannot stand in for.
        var coord = Add(patch, "coord");
        var module = Add(patch, typeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, module.Id, 0);
        patch.Connect(module.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        return t =>
        {
            program.Evaluate(t, 0d, 0d, registers, default);
            return Read(program, registers);
        };
    }

    /// <summary>A module taking a color and handing one back, read swatch by swatch.</summary>
    private static Func<float, float, float, (float R, float G, float B)> Through(
        string typeId, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var source = Add(patch, Rgb);
        var module = Add(patch, typeId, knobs);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(source.Id, 0, module.Id, 0);
        patch.Connect(module.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        program.AllocateRegisters();

        return (r, g, b) =>
        {
            source.InputValues[0] = r;
            source.InputValues[1] = g;
            source.InputValues[2] = b;

            // The knobs are compiled in, so a fresh program each swatch. Slow and
            // simple, which is the right way round for a test.
            var built = patch.CompileForVideo(Catalog).Program;
            var bank = built.AllocateRegisters();

            built.Evaluate(0d, 0d, 0d, bank, default);
            return Read(built, bank);
        };
    }

    /// <summary>One color taken apart, which is the whole of what To HSV is for.</summary>
    private static (float Hue, float Saturation, float Value) Taken(float r, float g, float b)
    {
        var patch = new Patch();

        var source = Add(patch, Rgb, (0, r), (1, g), (2, b));
        var taken = Add(patch, ToHsv);
        var built = Add(patch, Rgb);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(source.Id, 0, taken.Id, 0);

        // Back into an RGB, so all three readings come out of one program on the
        // three channels the sink already has.
        for (var port = 0; port < 3; port++) patch.Connect(taken.Id, port, built.Id, port);
        patch.Connect(built.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default);

        return Read(program, registers);
    }

    /// <summary>HSV in, a color, and the HSV read back off it.</summary>
    private static Func<float, float, float, (float Hue, float Saturation, float Value)> RoundTrip()
    {
        var patch = new Patch();

        var made = Add(patch, Hsv);
        var taken = Add(patch, ToHsv);
        var built = Add(patch, Rgb);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(made.Id, 0, taken.Id, 0);
        for (var port = 0; port < 3; port++) patch.Connect(taken.Id, port, built.Id, port);
        patch.Connect(built.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

        return (h, s, v) =>
        {
            made.InputValues[0] = h;
            made.InputValues[1] = s;
            made.InputValues[2] = v;

            var program = patch.CompileForVideo(Catalog).Program;
            var registers = program.AllocateRegisters();

            program.Evaluate(0d, 0d, 0d, registers, default);
            return Read(program, registers);
        };
    }

    /// <summary>The program for one module lit by nothing in particular, for the shader tests.</summary>
    private static CompiledPatch Lit(string typeId)
    {
        var patch = new Patch();

        var source = Add(patch, Rgb, (0, 0.4f), (1, 0.7f), (2, 0.2f));
        var module = Add(patch, typeId);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(source.Id, 0, module.Id, 0);
        patch.Connect(module.Id, 0, screen.Id, NodeCatalog.OutputColorPort);

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

    private static (float R, float G, float B) Read(CompiledPatch program, double[] registers) =>
        ((float)registers[program.OutputBase],
            (float)registers[program.OutputBase + 1],
            (float)registers[program.OutputBase + 2]);

    /// <summary>How far apart the channels are, which is how colored a color is.</summary>
    private static float Chroma((float R, float G, float B) color) =>
        Channels(color).Max() - Channels(color).Min();

    private static IEnumerable<float> Channels((float R, float G, float B) color)
    {
        yield return color.R;
        yield return color.G;
        yield return color.B;
    }

    /// <summary>Positions along a palette, off the ends so none of them is a special case.</summary>
    private static IEnumerable<float> Along(int count = 16) =>
        Enumerable.Range(0, count).Select(i => (i + 0.37f) / count);

    /// <summary>Colors to put through a module, including the corners and a few in between.</summary>
    private static IEnumerable<(float R, float G, float B)> Swatches()
    {
        yield return (0f, 0f, 0f);
        yield return (1f, 1f, 1f);
        yield return (1f, 0f, 0f);
        yield return (0f, 0.5f, 1f);
        yield return (0.2f, 0.7f, 0.35f);
        yield return (0.9f, 0.9f, 0.1f);
    }
}

internal static class ColorAssertions
{
    /// <summary>Three channels at once, so a failure names the color rather than a register.</summary>
    public static void ShouldBe(
        this (float R, float G, float B) actual, (float R, float G, float B) expected, float tolerance)
    {
        actual.R.ShouldBe(expected.R, tolerance, $"red of {actual}");
        actual.G.ShouldBe(expected.G, tolerance, $"green of {actual}");
        actual.B.ShouldBe(expected.B, tolerance, $"blue of {actual}");
    }
}
