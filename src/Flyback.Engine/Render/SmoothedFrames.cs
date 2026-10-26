using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// Draws through another renderer and smooths what it drew: <see cref="Smoothing.Fast"/>
/// passes over the frame, <see cref="Smoothing.Best"/> has it drawn at twice the size
/// and averages each four pixels into one.
/// </summary>
/// <remarks>
/// The renderer underneath keeps the history a feedback loop reads, so a loop
/// reads the frame as drawn and never smooths itself again each frame.
/// </remarks>
internal sealed class SmoothedFrames(IFrameRenderer frames, Smoothing smoothing) : IFrameRenderer
{
    private byte[] large = [];

    public void Render(
        CompiledPatch patch,
        double time,
        int width,
        int height,
        Span<byte> destination,
        int stride,
        LiveValues? live = null)
    {
        switch (smoothing)
        {
            case Smoothing.Fast:
                frames.Render(patch, time, width, height, destination, stride, live);
                Fxaa.Apply(destination, width, height, stride);
                break;

            case Smoothing.Best when width > 0 && height > 0:
                var wide = width * 2;
                if (large.Length != (long)wide * 4 * height * 2) large = new byte[(long)wide * 4 * height * 2];

                frames.Render(patch, time, wide, height * 2, large, wide * 4, live);
                Halve(large, wide * 4, width, height, destination, stride);
                break;

            default:
                frames.Render(patch, time, width, height, destination, stride, live);
                break;
        }
    }

    /// <summary>Whether a frame this size has room to be drawn at twice its width and height.</summary>
    public static bool FitsBest(int width, int height) => 4L * width * height <= SynthRenderer.MostPixels;

    /// <summary>Averages each two-by-two block of <paramref name="source"/> into one pixel of <paramref name="destination"/>.</summary>
    internal static void Halve(ReadOnlySpan<byte> source, int sourceStride, int width, int height, Span<byte> destination, int stride)
    {
        for (var y = 0; y < height; y++)
        {
            var top = source.Slice(y * 2 * sourceStride);
            var bottom = source.Slice((y * 2 + 1) * sourceStride);
            var row = destination.Slice(y * stride);

            for (var x = 0; x < width; x++)
                for (var c = 0; c < 4; c++)
                {
                    var (left, right) = (x * 8 + c, x * 8 + 4 + c);
                    row[x * 4 + c] = (byte)((top[left] + top[right] + bottom[left] + bottom[right] + 2) / 4);
                }
        }
    }
}
