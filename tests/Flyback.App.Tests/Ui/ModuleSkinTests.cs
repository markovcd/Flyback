using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// A plugin's own background: the colors it is worked out from, the picture it
/// may be instead, and the ink that has to stay readable over either.
/// </summary>
/// <remarks>
/// The contrast rule is the part worth testing rather than looking at. Inverting
/// a color is the obvious way to write on it and it fails silently in the middle
/// of the range — invisible text on a mid-gray, which nobody would catch by
/// opening one patch, because it needs the one background a plugin happened not
/// to pick.
/// </remarks>
public class ModuleSkinTests
{
    /// <summary>
    /// The whole promise of the max-visibility mode, over the whole cube: ink and
    /// background are always half the range apart in light.
    /// </summary>
    [Theory]
    [MemberData(nameof(AcrossTheCube))]
    public void Ink_is_always_half_the_range_from_what_it_is_written_on(byte red, byte green, byte blue)
    {
        var background = Color.FromRgb(red, green, blue);
        var ink = Colors.Contrast(background, lift: !Colors.Light(background));

        Math.Abs(Colors.Luma(ink) - Colors.Luma(background))
            .ShouldBeGreaterThanOrEqualTo(0.5 - Rounding, $"{background} was written on in {ink}");
    }

    /// <summary>What a byte of rounding per channel is worth in light.</summary>
    private const double Rounding = 0.01;

    public static TheoryData<byte, byte, byte> AcrossTheCube()
    {
        var data = new TheoryData<byte, byte, byte>();

        for (var red = 0; red < 256; red += 15)
        for (var green = 0; green < 256; green += 15)
        for (var blue = 0; blue < 256; blue += 15)
            data.Add((byte)red, (byte)green, (byte)blue);

        return data;
    }

    /// <summary>
    /// The worst a plain inverse does anywhere in the cube, and the whole reason
    /// the rule is not just an inverse: this olive inverts to a color of exactly
    /// its own luminance, which is text that cannot be seen at all.
    /// </summary>
    /// <remarks>
    /// An ordinary color somebody would pick, not a corner: the failure is the
    /// whole mid-luminance shell of the cube, and a gray is only its most obvious
    /// member.
    /// </remarks>
    [Fact]
    public void The_one_color_a_plain_inverse_cannot_be_read_on()
    {
        var olive = Color.FromRgb(0x73, 0x87, 0x5A);

        var inverse = Color.FromRgb(
            (byte)(255 - olive.R),
            (byte)(255 - olive.G),
            (byte)(255 - olive.B));

        Math.Abs(Colors.Luma(inverse) - Colors.Luma(olive))
            .ShouldBeLessThan(0.001, "this is the color the plain rule is invisible on");

        Math.Abs(Colors.Luma(Colors.Contrast(olive, lift: !Colors.Light(olive))) - Colors.Luma(olive))
            .ShouldBeGreaterThan(0.45);
    }

    /// <summary>
    /// And where the direction flips, either side reads. The flip is where both
    /// answers are worth the same, so neither side of it is the wrong one.
    /// </summary>
    [Theory]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    public void Both_sides_of_the_middle_are_written_on(byte level)
    {
        var gray = Color.FromRgb(level, level, level);

        Math.Abs(Colors.Luma(Colors.Contrast(gray, lift: !Colors.Light(gray))) - Colors.Luma(gray))
            .ShouldBeGreaterThan(0.45);
    }

    /// <summary>
    /// And where the inverse does separate, which is every background the engine
    /// itself draws, the ink is exactly the inverse — the rule drives a color
    /// only as far as it has to.
    /// </summary>
    [Fact]
    public void A_dark_background_is_written_on_in_its_own_inverse()
    {
        var ink = Colors.Contrast(Colors.Node, lift: true);

        ink.ShouldBe(Color.FromRgb(
            (byte)(255 - Colors.Node.R),
            (byte)(255 - Colors.Node.G),
            (byte)(255 - Colors.Node.B)));
    }

    // --- the palette --------------------------------------------------------

    [Fact]
    public void A_module_with_no_skin_is_drawn_as_its_category() =>
        Colors.Palette(Bare()).Accent.ShouldBe(Colors.Accent(ModuleCategories.Maths));

    [Fact]
    public void A_palette_stands_where_the_category_accent_would()
    {
        var def = Bare() with { Skin = new ModuleSkin.Palette(new Swatch(0x2E, 0x8B, 0x57)) };

        Colors.Palette(def).Accent.ShouldBe(Color.FromRgb(0x2E, 0x8B, 0x57));
    }

    /// <summary>One color is a palette: the wash falls to the accent it started from.</summary>
    [Fact]
    public void A_palette_of_one_color_falls_to_itself()
    {
        var def = Bare() with { Skin = new ModuleSkin.Palette(new Swatch(0x2E, 0x8B, 0x57)) };
        var (accent, floor) = Colors.Palette(def);

        floor.ShouldBe(accent);
    }

    [Fact]
    public void A_palette_of_two_falls_to_the_second()
    {
        var def = Bare() with
        {
            Skin = new ModuleSkin.Palette(new Swatch(0x2E, 0x8B, 0x57))
            {
                Floor = new Swatch(0x10, 0x20, 0x30),
            },
        };

        Colors.Palette(def).Floor.ShouldBe(Color.FromRgb(0x10, 0x20, 0x30));
    }

    /// <summary>
    /// A plugin's own mark stands where the category's would, and path data that
    /// will not read draws nothing rather than taking the canvas down (ADR-0025).
    /// </summary>
    [AvaloniaFact]
    public void A_plugin_may_draw_its_own_mark()
    {
        var mine = Bare() with
        {
            Skin = new ModuleSkin.Palette(new Swatch(0x40, 0x40, 0x40)) { Glyph = "M2,2 L22,22" },
        };

        ModuleGlyphs.For(mine).ShouldNotBe(ModuleGlyphs.For(Bare()));
    }

    [AvaloniaFact]
    public void A_mark_that_will_not_parse_draws_nothing()
    {
        var broken = Bare() with
        {
            Skin = new ModuleSkin.Palette(new Swatch(0x40, 0x40, 0x40)) { Glyph = "not a path at all" },
        };

        ModuleGlyphs.For(broken).ShouldBeNull();
    }

    // --- the grain ----------------------------------------------------------

    /// <summary>
    /// A grain is a palette with a texture over it, so everything worked out from
    /// a palette's colors is worked out from a grain's — the cut is a second
    /// channel rather than a different kind of background.
    /// </summary>
    [Fact]
    public void A_grain_is_a_palette()
    {
        var def = Bare() with
        {
            Skin = new ModuleSkin.Grain(new Swatch(0x2E, 0x8B, 0x57), GrainCut.Hatched),
        };

        def.Skin.ShouldBeAssignableTo<ModuleSkin.Palette>();
        Colors.Palette(def).Accent.ShouldBe(Color.FromRgb(0x2E, 0x8B, 0x57));
    }

    /// <summary>
    /// The three cuts are three cuts. They are the whole reason a grain exists —
    /// a channel that survives a grayscale screenshot — so two that drew the same
    /// would be one channel wearing three names.
    /// </summary>
    [AvaloniaFact]
    public void Every_cut_draws_something_of_its_own()
    {
        var over = NodeSkin.BodyTop(Color.FromRgb(0x2E, 0x8B, 0x57), selected: false);

        var drawn = Enum.GetValues<GrainCut>().Select(cut => NodeSkin.Cut(cut, over)).ToList();

        drawn.Distinct().Count().ShouldBe(drawn.Count);
    }

    /// <summary>Asked twice for the same cut, the same brush comes back.</summary>
    [AvaloniaFact]
    public void A_cut_is_made_once()
    {
        var over = NodeSkin.BodyTop(Color.FromRgb(0x2E, 0x8B, 0x57), selected: false);

        NodeSkin.Cut(GrainCut.Milled, over).ShouldBeSameAs(NodeSkin.Cut(GrainCut.Milled, over));
    }

    // --- the picture --------------------------------------------------------

    /// <summary>
    /// A still picture is one frame and no clock, and the bands it is read in are
    /// the color it actually is — this one is a flat amber square.
    /// </summary>
    [AvaloniaFact]
    public void A_still_picture_is_one_frame_read_at_its_own_color()
    {
        var art = ModuleArtwork.Of(new ModuleSkin.Artwork(Convert.FromBase64String(FlatPng)));

        art.ShouldNotBeNull();
        art.Frames.Count.ShouldBe(1);
        art.Runs.ShouldBe(0);

        art.Mean.R.ShouldBeInRange((byte)0xE8, (byte)0xF8);
        art.Mean.G.ShouldBeInRange((byte)0xD8, (byte)0xE8);
        art.Mean.B.ShouldBeInRange((byte)0x38, (byte)0x48);
    }

    /// <summary>An animated GIF keeps its frames and their timing.</summary>
    [AvaloniaFact]
    public void An_animated_picture_keeps_its_frames()
    {
        var art = ModuleArtwork.Of(new ModuleSkin.Artwork(Convert.FromBase64String(TwoFrameGif)));

        art.ShouldNotBeNull();
        art.Frames.Count.ShouldBe(2);
        art.Runs.ShouldBe(200);

        // Black first, white second — so the frame showing says which.
        art.At(50).ShouldBe(art.Frames[0]);
        art.At(150).ShouldBe(art.Frames[1]);
        art.At(250).ShouldBe(art.Frames[0], "the run should loop");
    }

    [AvaloniaFact]
    public void An_svg_is_read_as_a_picture()
    {
        var svg = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#204080"/></svg>""");

        var art = ModuleArtwork.Of(new ModuleSkin.Artwork(svg));

        art.ShouldNotBeNull();
        art.Frames.Count.ShouldBe(1);
        Colors.Light(art.Mean).ShouldBeFalse();
    }

    /// <summary>
    /// A skin may carry a second picture for the panel, the two surfaces being
    /// different shapes. The block keeps the first either way.
    /// </summary>
    [AvaloniaFact]
    public void A_second_picture_is_the_panel_s()
    {
        var dark = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#101820"/></svg>""");

        var skin = new ModuleSkin.Artwork(Convert.FromBase64String(FlatPng)) { Panel = dark };

        var block = ModuleArtwork.Of(skin).ShouldNotBeNull();
        var panel = ModuleArtwork.Of(skin, panel: true).ShouldNotBeNull();

        Colors.Light(block.Mean).ShouldBeTrue("the block keeps the amber");
        Colors.Light(panel.Mean).ShouldBeFalse("the panel takes the second picture");
    }

    /// <summary>One picture is both surfaces, and is read once for the two.</summary>
    [AvaloniaFact]
    public void One_picture_serves_the_block_and_the_panel()
    {
        var skin = new ModuleSkin.Artwork(Convert.FromBase64String(FlatPng));

        ModuleArtwork.Of(skin, panel: true).ShouldBeSameAs(ModuleArtwork.Of(skin));
    }

    /// <summary>
    /// Bytes that are not a picture are not a crash: the module falls back to its
    /// category, the way an unreadable mark draws nothing.
    /// </summary>
    [AvaloniaFact]
    public void Bytes_that_are_not_a_picture_are_no_picture() =>
        ModuleArtwork.Of(new ModuleSkin.Artwork(Encoding.UTF8.GetBytes("not a picture"))).ShouldBeNull();

    /// <summary>A four-pixel square of flat amber, filter type 0 on every row.</summary>
    private const string FlatPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAEUlEQVR4nGP48MABjhiI4wAAZWEhAY9tFLkAAAAASUVORK5CYII=";

    /// <summary>
    /// A GIF89a of two frames, black then white, a tenth of a second each, set to
    /// loop. One pixel a frame, which is what keeps the LZW stream to a clear
    /// code, a literal and an end marker at one code width.
    /// </summary>
    private const string TwoFrameGif =
        "R0lGODlhAQABAPAAAAAAAP///yH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAQABAAACAkQBACH5BAAKAAAALAAAAAABAAEAAAICTAEAOw==";

    // --- the panel ------------------------------------------------------------

    /// <summary>
    /// Plain text is not ContrastText's story to tell: a module that never asked
    /// for it keeps its description in the ordinary muted gray.
    /// </summary>
    [Fact]
    public void Description_ink_is_plain_without_contrast_text()
    {
        var def = Bare() with { Skin = new ModuleSkin.Palette(new Swatch(0x20, 0x20, 0x20)) };

        ModulePlate.BodyQuiet(def).ShouldBeSameAs(Text.Muted);
    }

    /// <summary>
    /// A module that asks for contrast text gets its description colored from
    /// its own body rather than the shell's ordinary muted gray — the same
    /// background the wash paints there (ADR-0118).
    /// </summary>
    [Fact]
    public void Description_ink_follows_contrast_text()
    {
        var def = Bare() with
        {
            Skin = new ModuleSkin.Palette(new Swatch(0x20, 0x20, 0x20)) { ContrastText = true },
        };

        ModulePlate.BodyQuiet(def).ShouldNotBeSameAs(Text.Muted);
    }

    /// <summary>A module with nothing said about how it is drawn.</summary>
    private static NodeDef Bare() => NodeCatalog.Require("math.add");
}
