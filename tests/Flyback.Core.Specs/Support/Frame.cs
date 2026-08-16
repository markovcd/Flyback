namespace Flyback.Core.Specs.Support;

/// <summary>
/// One rendered frame, kept with the size it was rendered at so a step can ask
/// about a point on the picture rather than about an index into a buffer.
/// </summary>
/// <remarks>
/// That distinction is the whole of what makes ADR-0014 testable. A patch is a
/// function of coordinates, and the frame is a sampling of it; an assertion
/// naming a pixel index can only ever be about one sampling, which is why the
/// suite had no way to say that two of them agree.
/// </remarks>
public sealed record Frame(byte[] Buffer, int Width, int Height)
{
    private int Stride => Width * 4;

    public (float R, float G, float B) Centre => At(Width / 2, Height / 2);

    public bool IsBlack
    {
        get
        {
            for (var i = 0; i < Buffer.Length; i += 4)
                if (Buffer[i] != 0 || Buffer[i + 1] != 0 || Buffer[i + 2] != 0)
                    return false;

            return true;
        }
    }

    /// <summary>The stored byte, before it is scaled back into 0..1 — see <see cref="At"/>.</summary>
    public byte RedByteAt(int x, int y) => Buffer[y * Stride + x * 4 + 2];

    /// <summary>The buffer is BGRA, so the channels come back in the opposite order.</summary>
    public (float R, float G, float B) At(int x, int y)
    {
        var pixel = y * Stride + x * 4;
        return (Buffer[pixel + 2] / 255f, Buffer[pixel + 1] / 255f, Buffer[pixel + 0] / 255f);
    }

    /// <summary>
    /// Samples by fraction of the frame rather than by pixel, so the same
    /// question can be put to two frames of different sizes.
    /// </summary>
    public (float R, float G, float B) AtFraction(float across, float down) =>
        At(Index(across, Width), Index(down, Height));

    /// <summary>
    /// How many pixels of the middle row, and of the middle column, are darker
    /// than half. On a frame showing one dark disc on a light field these are
    /// its width and its height, which is what "a circle stays a circle" means
    /// once it has been sampled onto a grid that is not square.
    /// </summary>
    public (int Across, int Down) DarkExtent()
    {
        var across = 0;
        var down = 0;

        for (var x = 0; x < Width; x++)
            if (At(x, Height / 2).R < 0.5f) across++;

        for (var y = 0; y < Height; y++)
            if (At(Width / 2, y).R < 0.5f) down++;

        return (across, down);
    }

    private static int Index(float fraction, int size) =>
        Math.Clamp((int)(fraction * size), 0, size - 1);
}
