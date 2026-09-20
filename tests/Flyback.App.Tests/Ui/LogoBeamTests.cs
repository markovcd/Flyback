using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Render;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The patch the About window's mark becomes. Nothing here is about the window:
/// what is worth checking is that it draws the mark, and that it is still
/// drawing the same one after it has been left running.
/// </summary>
public class LogoBeamTests
{
    private const int Side = 128;
    private const int Stride = Side * 4;

    /// <summary>The frame after <paramref name="frames"/> of it, from a standing start.</summary>
    private static byte[] Drawn(int frames)
    {
        var video = LogoBeam.Patch().CompileForVideo(samples: new SampleLibrary(), pictures: new ImageLibrary());

        video.HasErrors.ShouldBeFalse(string.Join("; ", video.Issues.Select(i => i.Message)));

        var renderer = new SynthRenderer();
        var pixels = new byte[Stride * Side];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(video.Program, frame / 30d, Side, Side, pixels, Stride);

        return pixels;
    }

    /// <summary>A point of the logo's own 256-unit box, as blue, green and red.</summary>
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
        var (blue, green, red) = Pixel(Drawn(200), 73, 190);

        red.ShouldBeInRange((byte)60, (byte)220);
        green.ShouldBeLessThan(red);
        blue.ShouldBeLessThan(red);
    }

    /// <summary>The beam has crossed the upper ramp, and left it behind, by the first flyback.</summary>
    [Fact]
    public void It_draws_the_ramp_the_beam_has_crossed()
    {
        var (_, green, _) = Pixel(Drawn(38), 150, 67);

        green.ShouldBeGreaterThan((byte)40);
    }

    /// <summary>
    /// Where the mark is not, the frame is the surface the dialog is drawn on:
    /// the box has to read as part of the window rather than a black tile cut
    /// into it.
    /// </summary>
    [Fact]
    public void It_grounds_the_frame_on_the_dialog_s_own_surface()
    {
        Pixel(Drawn(200), 220, 220).ShouldBe((Colors.Panel.B, Colors.Panel.G, Colors.Panel.R));
    }
}
