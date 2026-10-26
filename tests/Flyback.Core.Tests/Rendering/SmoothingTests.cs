using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// Smoothing a finished picture: a pass over the frame that blends across the edges
/// it finds, or four samples a pixel averaged down.
/// </summary>
public class SmoothingTests
{
    private const int Width = 48, Height = 32, Stride = Width * 4;

    [Fact]
    public void Off_is_the_frame_as_drawn()
    {
        Draw(Smoothing.Off).ShouldBe(Draw(new Slant()));
    }

    /// <summary>A hard edge drawn once a pixel is all black or all white; smoothed, some of it is between.</summary>
    [Theory]
    [InlineData(Smoothing.Fast)]
    [InlineData(Smoothing.Best)]
    public void A_slanted_edge_gets_pixels_between_its_two_sides(Smoothing smoothing)
    {
        Grays(Draw(new Slant())).ShouldAllBe(level => level == 0 || level == 255);

        Grays(Draw(smoothing)).Count(level => level is > 20 and < 235).ShouldBeGreaterThan(Height / 2);
    }

    /// <summary>Far from any edge, nothing changes.</summary>
    [Theory]
    [InlineData(Smoothing.Fast)]
    [InlineData(Smoothing.Best)]
    public void A_flat_picture_stays_flat(Smoothing smoothing)
    {
        var flat = new byte[Stride * Height];
        new SmoothedFrames(new Flat(), smoothing).Render(Program, 0d, Width, Height, flat, Stride);

        flat.Chunk(4).ShouldAllBe(pixel => pixel[0] == 40 && pixel[1] == 90 && pixel[2] == 160 && pixel[3] == 255);
    }

    [Fact]
    public void Best_is_the_frame_drawn_twice_as_large_averaged_in_fours()
    {
        var large = new byte[Stride * 2 * Height * 2];
        new Slant().Render(Program, 0d, Width * 2, Height * 2, large, Stride * 2);

        var halved = new byte[Stride * Height];
        SmoothedFrames.Halve(large, Stride * 2, Width, Height, halved, Stride);

        Draw(Smoothing.Best).ShouldBe(halved);
        halved[..4].ShouldBe(Average(large, 0, 0));
    }

    /// <summary>A frame four times the largest has no room, so the largest sizes cannot be drawn at Best.</summary>
    [Theory]
    [InlineData(3840, 2160, true)]
    [InlineData(4096, 4096, true)]
    [InlineData(4097, 4096, false)]
    [InlineData(8192, 8192, false)]
    public void Best_fits_up_to_a_quarter_of_the_largest_frame(int width, int height, bool fits)
    {
        SmoothedFrames.FitsBest(width, height).ShouldBe(fits);
    }

    /// <summary>The pass reads a texture's way, so a pixel past the border is the border's.</summary>
    [Fact]
    public void The_pass_leaves_the_frame_opaque_and_its_size()
    {
        var frame = Draw(Smoothing.Fast);

        frame.Length.ShouldBe(Stride * Height);
        frame.Chunk(4).ShouldAllBe(pixel => pixel[3] == 255);
    }

    private static readonly CompiledPatch Program = new Patch().CompileForVideo().Program;

    private static byte[] Draw(Smoothing smoothing) => Draw(new SmoothedFrames(new Slant(), smoothing));

    private static byte[] Draw(IFrameRenderer frames)
    {
        var pixels = new byte[Stride * Height];
        frames.Render(Program, 0d, Width, Height, pixels, Stride);

        return pixels;
    }

    private static IEnumerable<byte> Grays(byte[] frame) => frame.Chunk(4).Select(pixel => pixel[1]);

    private static byte[] Average(byte[] large, int x, int y)
    {
        var at = (int dx, int dy, int c) => large[(y * 2 + dy) * Stride * 2 + (x * 2 + dx) * 4 + c];

        return [.. Enumerable.Range(0, 4).Select(c => (byte)((at(0, 0, c) + at(1, 0, c) + at(0, 1, c) + at(1, 1, c) + 2) / 4))];
    }

    /// <summary>White on one side of a slanted line and black on the other, at whatever size it is asked for.</summary>
    private sealed class Slant : IFrameRenderer
    {
        public void Render(CompiledPatch patch, double time, int width, int height, Span<byte> destination, int stride, LiveValues? live = null)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var (u, v) = ((x + 0.5) / width, (y + 0.5) / height);
                    var level = (byte)(u > 0.3 + v * 0.37 ? 255 : 0);

                    destination.Slice(y * stride + x * 4, 4).Fill(level);
                    destination[y * stride + x * 4 + 3] = 255;
                }
        }
    }

    private sealed class Flat : IFrameRenderer
    {
        public void Render(CompiledPatch patch, double time, int width, int height, Span<byte> destination, int stride, LiveValues? live = null)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var pixel = destination.Slice(y * stride + x * 4, 4);
                    (pixel[0], pixel[1], pixel[2], pixel[3]) = (40, 90, 160, 255);
                }
        }
    }
}
