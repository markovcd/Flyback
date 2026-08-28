namespace Flyback.Core.Compile;

/// <summary>
/// A picture a patch has read in, held as the three floats a pixel is and read
/// at a position rather than at a pixel.
/// </summary>
/// <remarks>
/// <see cref="LoadedSample"/>'s counterpart, and deliberately its shape: a plain
/// array, immutable once made, read by an op at a position that is nearly always
/// between the samples it holds. What differs is the number of dimensions and
/// what lies outside — a clip runs off its ends into silence, and a picture runs
/// off all four of its edges into black, which is the same decision.
/// <para>
/// Held as float rather than as the bytes it was read from, because everything
/// downstream of it is float and converting once at load is cheaper than
/// converting four times a pixel. Three channels rather than four: alpha is
/// multiplied in as the file is read, so a transparent corner is a black one and
/// there is no fourth number for an op to have to carry.
/// </para>
/// <para>
/// The values are what the bytes said, divided by their maximum and nothing
/// else. No color management, no gamma, because the writer at the other end of
/// this does the same in reverse — so a frame rendered to a PNG and read back in
/// is the frame it was, to the eight bits it was written with.
/// </para>
/// </remarks>
/// <param name="Pixels">
/// Red, green and blue for every pixel, row by row from the top, which is the
/// order a PNG stores them and the order a frame buffer holds them.
/// </param>
public sealed record LoadedImage(float[] Pixels, int Width, int Height)
{
    /// <summary>How wide it is against how tall, which is the shape it is drawn at.</summary>
    public float Aspect => Height <= 0 ? 1f : (float)Width / Height;

    /// <summary>
    /// The color at a place, where the picture spans -1 to 1 downward and its
    /// own aspect either side of the middle — and black everywhere outside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placed at its own shape rather than stretched to the frame, so a picture
    /// is never squashed by the window it is being looked at in, and a frame
    /// this program exported and read back lands exactly where it came from. A
    /// patch that wants it stretched says so with a Scale, which is the same
    /// division of labour the Sample makes about time.
    /// </para>
    /// <para>
    /// Black outside rather than the edge held or the picture wrapped. Holding
    /// the edge smears the last row across everything beyond it, which reads as
    /// a fault; wrapping tiles the picture whether or not anybody asked, and
    /// tiling is something a patch says with a Tile. Running off the edge is how
    /// a picture ends, exactly as running off the end is how a clip does.
    /// </para>
    /// <para>
    /// Bilinear, like every other read in the machine that lands between what it
    /// holds. What that costs at the edges is half a pixel of the black outside
    /// bleeding into the last row, which is what a linear filter does on the
    /// shader too — and the two agreeing matters more here than either being
    /// right on its own.
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
/// <see cref="ISampleLibrary"/> again, for the same reasons word for word: the
/// compiler must not do file I/O, every edit recompiles the whole patch
/// (ADR-0021), and answering null is not an error but the thing a complaint is
/// made out of. Two interfaces rather than one with two methods, because a build
/// that can read a picture and not a sound is a real arrangement — the command
/// line drawing a still has no use for either, and something embedding the
/// engine may have only one.
/// </remarks>
public interface IImageLibrary
{
    /// <summary>The picture a path names, or null where there is none to be had.</summary>
    LoadedImage? Find(string path);

    /// <summary>Why the last <see cref="Find"/> of this path came back empty, for the complaint.</summary>
    string Explain(string path);
}
