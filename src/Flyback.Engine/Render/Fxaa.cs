using System.Numerics;

namespace Flyback.Engine.Render;

/// <summary>
/// Fast approximate anti-aliasing over a finished BGRA frame: where the light
/// changes sharply, the pixel is blended along the edge rather than across it.
/// </summary>
/// <remarks>
/// The same steps as the shaders' pass (<see cref="Compile.GlslEmitter.Smoothing"/>,
/// <see cref="Compile.MetalEmitter.SmoothingFragment"/>), reading bilinear and clamped
/// at the border as a texture is read, so a frame is smoothed alike wherever it is
/// drawn (ADR-0215).
/// </remarks>
internal static class Fxaa
{
    /// <summary>The least a blend reaches across an edge, so a faint one is still followed.</summary>
    public const float ReduceMin = 1f / 128f;

    /// <summary>How much the edge's own brightness narrows the blend.</summary>
    public const float ReduceMul = 1f / 8f;

    /// <summary>The farthest a blend reaches along an edge, in pixels.</summary>
    public const float SpanMax = 8f;

    /// <summary>Smooths <paramref name="frame"/> in place, BGRA, top row first.</summary>
    public static void Apply(Span<byte> frame, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;

        var source = new Vector3[width * height];

        for (var y = 0; y < height; y++)
        {
            var row = frame.Slice(y * stride, width * 4);

            for (var x = 0; x < width; x++)
                source[y * width + x] = new Vector3(row[x * 4 + 2], row[x * 4 + 1], row[x * 4]) / 255f;
        }

        var target = new Vector3[width * height];

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
                target[y * width + x] = At(source, width, height, x + 0.5f, y + 0.5f);
        });

        for (var y = 0; y < height; y++)
        {
            var row = frame.Slice(y * stride, width * 4);

            for (var x = 0; x < width; x++)
            {
                var color = target[y * width + x];
                row[x * 4 + 0] = ToByte(color.Z);
                row[x * 4 + 1] = ToByte(color.Y);
                row[x * 4 + 2] = ToByte(color.X);
                row[x * 4 + 3] = 255;
            }
        }
    }

    /// <summary>The smoothed color at a pixel's center, in pixels from the top left.</summary>
    private static Vector3 At(Vector3[] source, int width, int height, float x, float y)
    {
        var middle = Sample(source, width, height, x, y);
        var (nw, ne) = (Luma(Sample(source, width, height, x - 1f, y - 1f)), Luma(Sample(source, width, height, x + 1f, y - 1f)));
        var (sw, se) = (Luma(Sample(source, width, height, x - 1f, y + 1f)), Luma(Sample(source, width, height, x + 1f, y + 1f)));
        var m = Luma(middle);

        var least = MathF.Min(m, MathF.Min(MathF.Min(nw, ne), MathF.Min(sw, se)));
        var most = MathF.Max(m, MathF.Max(MathF.Max(nw, ne), MathF.Max(sw, se)));

        // Across the edge is where the light changes; along it is at right angles.
        var dx = -(nw + ne - (sw + se));
        var dy = nw + sw - (ne + se);

        var reduce = MathF.Max((nw + ne + sw + se) * 0.25f * ReduceMul, ReduceMin);
        var scale = 1f / (MathF.Min(MathF.Abs(dx), MathF.Abs(dy)) + reduce);
        dx = Math.Clamp(dx * scale, -SpanMax, SpanMax);
        dy = Math.Clamp(dy * scale, -SpanMax, SpanMax);

        var inner = (Sample(source, width, height, x + dx * (1f / 3f - 0.5f), y + dy * (1f / 3f - 0.5f))
            + Sample(source, width, height, x + dx * (2f / 3f - 0.5f), y + dy * (2f / 3f - 0.5f))) * 0.5f;
        var outer = inner * 0.5f
            + (Sample(source, width, height, x - dx * 0.5f, y - dy * 0.5f) + Sample(source, width, height, x + dx * 0.5f, y + dy * 0.5f)) * 0.25f;

        // The wider blend overshot the light around the pixel, so it crossed another edge.
        var lumaOuter = Luma(outer);

        return lumaOuter < least || lumaOuter > most ? inner : outer;
    }

    /// <summary>A bilinear read at a point in pixels, clamped to the edge as a texture is.</summary>
    private static Vector3 Sample(Vector3[] source, int width, int height, float x, float y)
    {
        var fx = Math.Clamp(x - 0.5f, 0f, width - 1);
        var fy = Math.Clamp(y - 0.5f, 0f, height - 1);

        var (x0, y0) = ((int)fx, (int)fy);
        var (x1, y1) = (Math.Min(x0 + 1, width - 1), Math.Min(y0 + 1, height - 1));
        var (tx, ty) = (fx - x0, fy - y0);

        var top = Vector3.Lerp(source[y0 * width + x0], source[y0 * width + x1], tx);
        var bottom = Vector3.Lerp(source[y1 * width + x0], source[y1 * width + x1], tx);

        return Vector3.Lerp(top, bottom, ty);
    }

    private static float Luma(Vector3 color) => color.X * 0.299f + color.Y * 0.587f + color.Z * 0.114f;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
