namespace Flyback.Editor.Capture;

/// <summary>Turns a frame as OpenGL reads it back into one as a file or another program takes it.</summary>
internal static class GlFrame
{
    /// <summary>
    /// Writes <paramref name="rgba"/>, RGBA with the first row at the bottom, into
    /// <paramref name="bgra"/> as BGRA with the first row at the top, tightly packed.
    /// </summary>
    public static void ToBgra(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> bgra)
    {
        var stride = width * 4;

        for (var y = 0; y < height; y++)
        {
            var source = rgba.Slice((height - 1 - y) * stride, stride);
            var target = bgra.Slice(y * stride, stride);

            for (var x = 0; x < stride; x += 4)
            {
                target[x] = source[x + 2];
                target[x + 1] = source[x + 1];
                target[x + 2] = source[x];
                target[x + 3] = source[x + 3];
            }
        }
    }
}
