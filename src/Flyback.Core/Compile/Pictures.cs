namespace Flyback.Core.Compile;

/// <summary>
/// A picture a patch has read in, held as the three floats a pixel is and read at
/// a position rather than at a pixel.
/// </summary>
/// <remarks>
/// <see cref="LoadedSample"/>'s counterpart and deliberately its shape. What
/// differs is the number of dimensions: a clip runs off its ends into silence, a
/// picture off all four edges into black. Float rather than the bytes it was read
/// from, because everything downstream is float; three channels, because alpha is
/// multiplied in as the file is read.
/// <para>
/// The values are what the bytes said, divided by their maximum and nothing else
/// — no color management, no gamma — because the writer at the other end does the
/// same in reverse.
/// </para>
/// </remarks>
/// <param name="Pixels">
/// Red, green and blue for every pixel, row by row from the top, which is the
/// order a PNG stores them and a frame buffer holds them.
/// </param>
public sealed record LoadedImage(float[] Pixels, int Width, int Height)
{
    /// <summary>How wide it is against how tall, which is the shape it is drawn at.</summary>
    public float Aspect => Height <= 0 ? 1f : (float)Width / Height;

    /// <summary>
    /// The color at a place, where the picture spans -1 to 1 downward and its own
    /// aspect either side of the middle — and black everywhere outside it.
    /// </summary>
    /// <remarks>
    /// Placed at its own shape rather than stretched to the frame, so a picture is
    /// never squashed by the window it is looked at in; a patch that wants it
    /// stretched says so with a Scale. Black outside rather than the edge held or
    /// the picture wrapped: holding the edge smears the last row, and tiling is
    /// something a patch says with a Tile.
    /// <para>
    /// Bilinear, like every other read that lands between what it holds. At the
    /// edges that bleeds half a pixel of the outside black into the last row,
    /// which is what the shader's filter does too — and the two agreeing matters
    /// more than either being right alone.
    /// </para>
    /// </remarks>
    public void At(double x, double y, Span<double> rgb)
    {
        rgb[0] = rgb[1] = rgb[2] = 0d;

        if (Width < 1 || Height < 1 || Pixels.Length < Width * Height * 3) return;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        // Into the picture's own square, where 0,0 is the top left corner and
        // 1,1 the bottom right: y runs down a picture and up a frame.
        var u = (x / Aspect + 1d) * 0.5d;
        var v = (1d - y) * 0.5d;

        if (u < 0d || u > 1d || v < 0d || v > 1d) return;

        // Clamped to the last pixel exactly rather than a hair short of it. The
        // feedback sampler stops short because it indexes the row after without
        // holding it; the pair below is clamped on its own, so landing on the
        // last row here weights it fully and a corner of the picture is that
        // corner rather than a ten-thousandth of the row above.
        var fx = Math.Clamp(u * Width - 0.5d, 0d, Width - 1d);
        var fy = Math.Clamp(v * Height - 0.5d, 0d, Height - 1d);

        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);

        double tx = fx - x0, ty = fy - y0;

        var i00 = (y0 * Width + x0) * 3;
        var i10 = (y0 * Width + x1) * 3;
        var i01 = (y1 * Width + x0) * 3;
        var i11 = (y1 * Width + x1) * 3;

        for (var c = 0; c < 3; c++)
        {
            var top = Pixels[i00 + c] + (Pixels[i10 + c] - Pixels[i00 + c]) * tx;
            var bottom = Pixels[i01 + c] + (Pixels[i11 + c] - Pixels[i01 + c]) * tx;

            rgb[c] = top + (bottom - top) * ty;
        }
    }
}

/// <summary>
/// Where a patch's pictures come from. The compiler asks; something outside it
/// answers, and owns the reading and the caching.
/// </summary>
/// <remarks>
/// <see cref="ISampleLibrary"/> again and for the same reasons: the compiler must
/// not do file I/O, every edit recompiles the whole patch (ADR-0021), and
/// answering null is what a complaint is made out of. Two interfaces rather than
/// one, because a build that can read a picture and not a sound is a real
/// arrangement.
/// </remarks>
public interface IImageLibrary
{
    /// <summary>The picture a path names, or null where there is none to be had.</summary>
    LoadedImage? Find(string path);

    /// <summary>Why the last <see cref="Find"/> of this path came back empty, for the complaint.</summary>
    string Explain(string path);
}
