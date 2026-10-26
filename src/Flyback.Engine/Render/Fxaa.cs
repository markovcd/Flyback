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

    /// <summary>The least contrast between a pixel and its corners that counts as an edge.</summary>
    public const float EdgeMin = 1f / 32f;

    /// <summary>The share of the brightest corner that contrast must also reach, so a bright area needs a stronger edge.</summary>
    public const float EdgeMul = 1f / 8f;

    /// <summary>The frame as colors and as light, kept for the next frame the preview's thread smooths.</summary>
    [ThreadStatic]
    private static Vector3[]? colors;

    [ThreadStatic]
    private static float[]? lights;

    /// <summary>Smooths <paramref name="frame"/> in place, BGRA, top row first.</summary>
    public static unsafe void Apply(Span<byte> frame, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;

        if (colors is null || colors.Length != width * height) (colors, lights) = (new Vector3[width * height], new float[width * height]);

        var (source, light) = (colors, lights!);

        fixed (byte* pinned = frame)
        {
            var origin = (nint)pinned;

            Parallel.For(0, height, y =>
            {
                var row = (byte*)origin + (nint)y * stride;

                for (var x = 0; x < width; x++)
                {
                    var color = new Vector3(row[x * 4 + 2], row[x * 4 + 1], row[x * 4]) / 255f;
                    source[y * width + x] = color;
                    light[y * width + x] = Luma(color);
                }
            });

            Parallel.For(0, height, y =>
            {
                var row = (byte*)origin + (nint)y * stride;
                var (up, down) = (Math.Max(y - 1, 0) * width, Math.Min(y + 1, height - 1) * width);

                for (var x = 0; x < width; x++)
                {
                    var (left, right) = (Math.Max(x - 1, 0), Math.Min(x + 1, width - 1));
                    var corners = (NW: light[up + left], NE: light[up + right], SW: light[down + left], SE: light[down + right]);
                    var m = light[y * width + x];

                    var least = MathF.Min(m, MathF.Min(MathF.Min(corners.NW, corners.NE), MathF.Min(corners.SW, corners.SE)));
                    var most = MathF.Max(m, MathF.Max(MathF.Max(corners.NW, corners.NE), MathF.Max(corners.SW, corners.SE)));

                    // Most of a picture is no edge at all, and is left as it was drawn.
                    if (most - least < MathF.Max(EdgeMin, most * EdgeMul)) continue;

                    var color = Along(source, width, height, x + 0.5f, y + 0.5f, corners, least, most);
                    row[x * 4 + 0] = ToByte(color.Z);
                    row[x * 4 + 1] = ToByte(color.Y);
                    row[x * 4 + 2] = ToByte(color.X);
                    row[x * 4 + 3] = 255;
                }
            });
        }
    }

    /// <summary>The color blended along the edge at a pixel's center, in pixels from the top left.</summary>
    private static Vector3 Along(
        Vector3[] source, int width, int height, float x, float y, (float NW, float NE, float SW, float SE) corners, float least, float most)
    {
        var (nw, ne, sw, se) = corners;

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
