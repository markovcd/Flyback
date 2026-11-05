using System.Runtime.CompilerServices;

namespace Flyback.Engine.Compile;

/// <summary>
/// Hash-based value noise. Deterministic and stateless, so it costs nothing to
/// evaluate per pixel and needs no permutation table to be shipped or seeded.
/// </summary>
internal static class Noise
{
    /// <summary>Smooth 3D value noise in the range 0..1.</summary>
    public static double Value3(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return 0d;

        var xf = Math.Floor(x);
        var yf = Math.Floor(y);
        var zf = Math.Floor(z);

        var xi = Lattice(xf);
        var yi = Lattice(yf);
        var zi = Lattice(zf);

        return Blend(
            Fade(x - xf), Fade(y - yf), Fade(z - zf),
            Hash(xi, yi, zi), Hash(xi + 1, yi, zi), Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi),
            Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1));
    }

    /// <summary>How many numbers <see cref="Value3(double, double, double, ref double)"/> keeps: a cell's corner and its eight hashes.</summary>
    public const int Kept = 11;

    /// <summary>
    /// <see cref="Value3(double, double, double)"/>, keeping the hashes of the cell it was
    /// last read in at <paramref name="kept"/>, so a read in the same cell hashes nothing.
    /// </summary>
    public static double Value3(double x, double y, double z, ref double kept)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return 0d;

        var xf = Math.Floor(x);
        var yf = Math.Floor(y);
        var zf = Math.Floor(z);

        if (BitConverter.DoubleToInt64Bits(kept) != BitConverter.DoubleToInt64Bits(xf)
            || BitConverter.DoubleToInt64Bits(Unsafe.Add(ref kept, 1)) != BitConverter.DoubleToInt64Bits(yf)
            || BitConverter.DoubleToInt64Bits(Unsafe.Add(ref kept, 2)) != BitConverter.DoubleToInt64Bits(zf))
            Keep(xf, yf, zf, ref kept);

        return Blend(
            Fade(x - xf), Fade(y - yf), Fade(z - zf),
            Unsafe.Add(ref kept, 3), Unsafe.Add(ref kept, 4), Unsafe.Add(ref kept, 5), Unsafe.Add(ref kept, 6),
            Unsafe.Add(ref kept, 7), Unsafe.Add(ref kept, 8), Unsafe.Add(ref kept, 9), Unsafe.Add(ref kept, 10));
    }

    /// <summary>Writes the cell at <paramref name="xf"/>, <paramref name="yf"/>, <paramref name="zf"/> and its corners' hashes at <paramref name="kept"/>.</summary>
    public static void Keep(double xf, double yf, double zf, ref double kept)
    {
        var xi = Lattice(xf);
        var yi = Lattice(yf);
        var zi = Lattice(zf);

        kept = xf;
        Unsafe.Add(ref kept, 1) = yf;
        Unsafe.Add(ref kept, 2) = zf;
        Unsafe.Add(ref kept, 3) = Hash(xi, yi, zi);
        Unsafe.Add(ref kept, 4) = Hash(xi + 1, yi, zi);
        Unsafe.Add(ref kept, 5) = Hash(xi, yi + 1, zi);
        Unsafe.Add(ref kept, 6) = Hash(xi + 1, yi + 1, zi);
        Unsafe.Add(ref kept, 7) = Hash(xi, yi, zi + 1);
        Unsafe.Add(ref kept, 8) = Hash(xi + 1, yi, zi + 1);
        Unsafe.Add(ref kept, 9) = Hash(xi, yi + 1, zi + 1);
        Unsafe.Add(ref kept, 10) = Hash(xi + 1, yi + 1, zi + 1);
    }

    /// <summary>The eight corners' hashes, named by which of x, y and z is one past the cell, blended at the faded offsets.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Blend(
        double u, double v, double w,
        double h000, double h100, double h010, double h110,
        double h001, double h101, double h011, double h111)
    {
        var z0 = Lerp(Lerp(h000, h100, u), Lerp(h010, h110, u), v);
        var z1 = Lerp(Lerp(h001, h101, u), Lerp(h011, h111, u), v);

        return Lerp(z0, z1, w);
    }

    /// <summary>
    /// A lattice index wrapped to 32 bits rather than saturated, so noise read off
    /// a fast clock is still noise days in. Inside an int's range it is the plain cast.
    /// </summary>
    /// <remarks>
    /// Past a long the cast saturates, so there the remainder is taken in doubles,
    /// which is exact for a whole number; the shader and the script wrap the same way.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Lattice(double floored) =>
        Math.Abs(floored) < 9.2e18
            ? unchecked((int)(long)floored)
            : unchecked((int)(uint)(floored - 4294967296d * Math.Floor(floored / 4294967296d)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Fade(double t) => t * t * (3d - 2d * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Hash(int x, int y, int z)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) * (1d / 0xFFFFFF);
    }
}
