using System.Runtime.CompilerServices;

namespace Flyback.Engine.Compile;

/// <summary>
/// The guarded math the interpreter and the IL backend both call, so a zero
/// divisor or a non-finite result is answered in one place (ADR-0013, ADR-0076).
/// </summary>
internal static class Arithmetic
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Guard(double v) => double.IsFinite(v) ? v : 0d;

    /// <summary>
    /// -1, 0 or 1, and 0 for anything that is not a number.
    /// <see cref="Math.Sign(double)"/> raises on a NaN, which is the one answer an
    /// op may not give.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Signum(double v) => v > 0d ? 1d : v < 0d ? -1d : 0d;

    /// <summary>
    /// Feedback held below one. At exactly one a delay line never decays and at
    /// more than one it doubles every pass, and that damage persists after the
    /// knob is turned back down.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Feedback(double v) => double.IsFinite(v) ? Math.Clamp(v, -0.99d, 0.99d) : 0d;

    /// <summary>
    /// What may be put in a plane, which is <see cref="DelayState.WritePlane"/>'s
    /// bound written again for the other sink's float.
    /// </summary>
    /// <remarks>
    /// A cycle drawn as wires carries no gain of its own, so a loop above unity
    /// is easy to draw and this is where it stops. Clamping rather than refusing
    /// leaves a runaway loop pinned at the rails — a white pixel — instead of
    /// turning it into a NaN that spreads.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Bounded(double v) =>
        double.IsFinite(v) ? (float)Math.Clamp(v, -16d, 16d) : 0f;

    /// <summary>
    /// The largest double below 1. For a tiny negative input, <c>v - floor(v)</c>
    /// is mathematically just under 1 but cancels to exactly 1.0 at any finite
    /// precision, and Fract is half-open — Saw and Tile both read it that way.
    /// </summary>
    private const double JustBelowOne = 0.99999999999999989d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Fract(double v)
    {
        var fraction = v - Math.Floor(v);
        return fraction < 1d ? fraction : JustBelowOne;
    }

    // Exact equality is the point in the three guards below: they trap the one
    // divisor that makes the result undefined. A tolerance would be wrong —
    // Divide(1, 1e-20f) is a legitimate 1e20, and an epsilon would flatten it to
    // zero. Guard already handles the overflow.
    // ReSharper disable CompareOfFloatsByEqualityOperator

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Divide(double a, double b) => b == 0d ? 0d : Guard(a / b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Modulo(double a, double b) => b == 0d ? 0d : Guard(a - b * Math.Floor(a / b));

    internal static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x < edge0 ? 0d : 1d;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    // ReSharper restore CompareOfFloatsByEqualityOperator

    internal static void HsvToRgb(double h, double s, double v, Span<double> rgb)
    {
        h = Fract(h) * 6d;
        s = Math.Clamp(s, 0d, 1d);

        var sector = (int)h;
        var f = h - sector;
        var p = v * (1d - s);
        var q = v * (1d - s * f);
        var t = v * (1d - s * (1d - f));

        (rgb[0], rgb[1], rgb[2]) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }

    /// <summary>Bilinear read of the previous frame in patch coordinates, clamped at the edges.</summary>
    internal static void Sample(in FeedbackFrame frame, double u, double v, Span<double> rgb)
    {
        var pixels = frame.Pixels;
        if (pixels is null || frame.Width < 2 || frame.Height < 2)
        {
            rgb[0] = rgb[1] = rgb[2] = 0d;
            return;
        }

        var fx = (u / frame.Aspect * 0.5d + 0.5d) * (frame.Width - 1);
        var fy = (0.5d - v * 0.5d) * (frame.Height - 1);

        fx = Math.Clamp(double.IsFinite(fx) ? fx : 0d, 0d, frame.Width - 1.001d);
        fy = Math.Clamp(double.IsFinite(fy) ? fy : 0d, 0d, frame.Height - 1.001d);

        int x0 = (int)fx, y0 = (int)fy;
        double tx = fx - x0, ty = fy - y0;

        var row0 = y0 * frame.Width;
        var row1 = row0 + frame.Width;
        var i00 = (row0 + x0) * 3;
        var i10 = i00 + 3;
        var i01 = (row1 + x0) * 3;
        var i11 = i01 + 3;

        for (var c = 0; c < 3; c++)
        {
            var top = pixels[i00 + c] + (pixels[i10 + c] - pixels[i00 + c]) * tx;
            var bottom = pixels[i01 + c] + (pixels[i11 + c] - pixels[i01 + c]) * tx;
            rgb[c] = top + (bottom - top) * ty;
        }
    }
}
