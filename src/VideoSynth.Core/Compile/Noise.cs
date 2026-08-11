using System.Runtime.CompilerServices;

namespace VideoSynth.Core.Compile;

/// <summary>
/// Hash-based value noise. Deterministic and stateless, so it costs nothing to
/// evaluate per pixel and needs no permutation table to be shipped or seeded.
/// </summary>
internal static class Noise
{
    /// <summary>Smooth 3D value noise in the range 0..1.</summary>
    public static float Value3(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return 0f;

        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var zi = (int)MathF.Floor(z);

        var u = Fade(x - xi);
        var v = Fade(y - yi);
        var w = Fade(z - zi);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * (3f - 2f * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hash(int x, int y, int z)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) * (1f / 0xFFFFFF);
    }
}
