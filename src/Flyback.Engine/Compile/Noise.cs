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

        var u = Fade(x - xf);
        var v = Fade(y - yf);
        var w = Fade(z - zf);

        var z0 = Lerp(
            Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), u),
            Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), u),
            v);

        var z1 = Lerp(
            Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), u),
            Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), u),
            v);

        return Lerp(z0, z1, w);
    }

    /// <summary>How <see cref="Value3"/> moves per unit of x, y and z.</summary>
    public static (double X, double Y, double Z) Slope3(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return default;

        var xf = Math.Floor(x);
        var yf = Math.Floor(y);
        var zf = Math.Floor(z);

        var xi = Lattice(xf);
        var yi = Lattice(yf);
        var zi = Lattice(zf);

        double fx = x - xf, fy = y - yf, fz = z - zf;
        double u = Fade(fx), v = Fade(fy), w = Fade(fz);

        double h000 = Hash(xi, yi, zi), h100 = Hash(xi + 1, yi, zi);
        double h010 = Hash(xi, yi + 1, zi), h110 = Hash(xi + 1, yi + 1, zi);
        double h001 = Hash(xi, yi, zi + 1), h101 = Hash(xi + 1, yi, zi + 1);
        double h011 = Hash(xi, yi + 1, zi + 1), h111 = Hash(xi + 1, yi + 1, zi + 1);

        var du = Lerp(Lerp(h100 - h000, h110 - h010, v), Lerp(h101 - h001, h111 - h011, v), w);
        var dv = Lerp(
            Lerp(h010, h110, u) - Lerp(h000, h100, u),
            Lerp(h011, h111, u) - Lerp(h001, h101, u),
            w);
        var z0 = Lerp(Lerp(h000, h100, u), Lerp(h010, h110, u), v);
        var z1 = Lerp(Lerp(h001, h101, u), Lerp(h011, h111, u), v);

        return (du * FadeSlope(fx), dv * FadeSlope(fy), (z1 - z0) * FadeSlope(fz));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double FadeSlope(double t) => 6d * t * (1d - t);

    /// <summary>
    /// A lattice index wrapped to 32 bits rather than saturated, so noise read off
    /// a fast clock is still noise days in. Inside an int's range it is the plain cast.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Lattice(double floored) => unchecked((int)(long)floored);

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
