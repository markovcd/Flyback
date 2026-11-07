using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Controls;

/// <summary>
/// The patch the About window's mark becomes. Nothing here is about the window:
/// what is worth checking is that the close-up draws the mark, that the whole
/// scene draws the set around it, and that the scene plays.
/// </summary>
public class LogoSceneTests
{
    private const int Side = 128;
    private const int Stride = Side * 4;

    /// <summary>The frame after <paramref name="frames"/> of it, from a standing start.</summary>
    private static byte[] Drawn(Patch patch, int frames, int width = Side, int height = Side)
    {
        var video = patch.CompileForVideo(samples: new SampleLibrary(), pictures: new ImageLibrary());

        video.HasErrors.ShouldBeFalse(string.Join("; ", video.Issues.Select(i => i.Message)));

        var renderer = new SynthRenderer();
        var pixels = new byte[width * 4 * height];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(video.Program, frame / 30d, width, height, pixels, width * 4);

        return pixels;
    }

    /// <summary>A point of the logo's own 256-unit box on the close-up, as blue, green and red.</summary>
    private static (byte B, byte G, byte R) Pixel(byte[] frame, int x, int y)
    {
        var at = y * Side / 256 * Stride + x * Side / 256 * 4;

        return (frame[at], frame[at + 1], frame[at + 2]);
    }

    /// <summary>
    /// Read low on the retrace, below where either ramp starts, so the bar is the
    /// only thing ever drawn there. A color inked after the Trails is added to its
    /// own echo every frame and ends up a white bar rather than a red one, which
    /// takes a couple of seconds to show — hence the long run.
    /// </summary>
    [Fact]
    public void It_draws_the_retrace_in_its_own_color_however_long_it_runs()
    {
        var (blue, green, red) = Pixel(Drawn(LogoScene.Framed(), 120), 73, 190);

        red.ShouldBeInRange((byte)60, (byte)220);
        green.ShouldBeLessThan(red);
        blue.ShouldBeLessThan(red);
    }

    /// <summary>The beam has crossed the upper ramp, and left it behind, by the first flyback.</summary>
    [Fact]
    public void It_draws_the_ramp_the_beam_has_crossed()
    {
        var (_, green, _) = Pixel(Drawn(LogoScene.Framed(), 38), 150, 67);

        green.ShouldBeGreaterThan((byte)40);
    }

    /// <summary>
    /// The corners of the close-up are off the glass, on the set's black bezel,
    /// darker than anything the tube does. Read in the very corner, well past the
    /// rim's own soft edge.
    /// </summary>
    [Fact]
    public void It_leaves_the_corners_of_the_close_up_to_the_bezel()
    {
        var frame = Drawn(LogoScene.Framed(), 60);
        var (glassBlue, glassGreen, glassRed) = Pixel(frame, 170, 200);
        var (blue, green, red) = Pixel(frame, 250, 6);

        (blue + green + red).ShouldBeLessThan(glassBlue + glassGreen + glassRed);
    }

    /// <summary>
    /// Pulled back, the tube is one part of a wooden set: the cabinet beside the
    /// screen is warm, red over green over blue, and the wall above it is not.
    /// </summary>
    [Fact]
    public void The_whole_scene_stands_the_tube_in_a_wooden_set()
    {
        const int width = 320;
        const int height = 180;

        var frame = Drawn(LogoScene.Patch(), 2, width, height);

        // The cabinet right of the screen, between the knobs and the bezel, and
        // the wall left of the set.
        var (blue, green, red) = At(frame, width, width * 0.5 + height * 0.5 * 0.33, height * 0.5 * (1 - 0.4));
        var (wallBlue, _, wallRed) = At(frame, width, width * 0.5 - height * 0.5 * 1.2, height * 0.5 * (1 - 0.3));

        red.ShouldBeGreaterThan(green);
        green.ShouldBeGreaterThan(blue);
        wallBlue.ShouldBeGreaterThan(wallRed);
    }

    private static (byte B, byte G, byte R) At(byte[] frame, int width, double x, double y)
    {
        var at = (int)y * width * 4 + (int)x * 4;

        return (frame[at], frame[at + 1], frame[at + 2]);
    }

    /// <summary>The scene is heard as well as seen: the loop plays from its first beat.</summary>
    [Fact]
    public void The_scene_plays_its_loop()
    {
        var audio = LogoScene.Patch().CompileForAudio(NodeCatalog.BuiltIn);

        audio.HasErrors.ShouldBeFalse(string.Join("; ", audio.Issues.Select(i => i.Message)));

        var renderer = new AudioRenderer();
        var buffer = new float[renderer.SampleRate * NodeCatalog.AudioChannels];

        renderer.Render(audio.Program, buffer);

        var loudest = buffer.Max(Math.Abs);

        loudest.ShouldBeGreaterThan(0.1f, "a second of the loop is not silence");
        loudest.ShouldBeLessThanOrEqualTo(1f);
    }
}
