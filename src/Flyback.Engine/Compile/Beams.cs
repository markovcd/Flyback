using System.Buffers;
using Flyback.Core.Graph;
using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// Draws what a Beam's two taps played as the trace an X-Y oscilloscope leaves on
/// its phosphor, into the buffer the picture reads.
/// </summary>
/// <remarks>
/// The beam moves at constant speed between two evaluations and lays down the same
/// energy every evaluation, so a stretch it crosses slowly is bright and a jump is
/// all but invisible. The phosphor fades exponentially with the age of what was
/// drawn, so the picture depends only on the ring and not on the frame rate. A
/// second, coarser copy is blurred for the halo (ADR-0181).
/// </remarks>
public static class Beams
{
    /// <summary>How many texels the trace is drawn across, either way.</summary>
    public const int Size = NodeCatalog.BeamSize;

    /// <summary>How many texels the halo is drawn across, either way.</summary>
    public const int GlowSize = NodeCatalog.BeamGlowSize;

    /// <summary>Where the halo starts in the buffer, after the trace.</summary>
    public const int GlowStart = Size * Size;

    /// <summary>
    /// The most evaluations drawn a frame, which bounds what a long persistence costs:
    /// two thirds of a second at the 2x rate, and the oldest of it already faint.
    /// </summary>
    public const int MaxSpan = 1 << 16;

    /// <summary>How many persistences back are drawn before the rest is too faint to see.</summary>
    private const float Reach = 5f;

    /// <summary>The beam's width, as a standard deviation in texels of the trace.</summary>
    private const float Focus = 1.1f;

    private const int Shrink = Size / GlowSize;

    /// <summary>How many texels of travel a frame draws before evaluations are skipped.</summary>
    private const double Budget = 40_000d;

    /// <summary>How far apart, in texels, the spots a line is drawn with are.</summary>
    private const float Spacing = 1f;

    /// <summary>How many texels a spot reaches either side of its own, and how wide that makes it.</summary>
    private const int Radius = 5;

    private const int Width = Radius * 2;

    /// <summary>
    /// A buffer for one Beam: the trace's texels row by row from the bottom, then
    /// the halo's. Its rate is one, so a table read's position is a texel's index.
    /// </summary>
    public static LoadedSample Buffer() => new(new float[GlowStart + GlowSize * GlowSize], 1);

    /// <summary>
    /// Draws the newest evaluations of traces <paramref name="across"/> and
    /// <paramref name="up"/> into <paramref name="into"/>, the window being how long
    /// the phosphor takes to fade to a third.
    /// </summary>
    /// <remarks>
    /// A value of one is the full height of the picture from its middle. What is
    /// drawn reads as one over the length of the trace, in picture heights, for a
    /// figure the beam keeps retracing; a beam held still for the whole window is
    /// the brightest it gets.
    /// </remarks>
    /// <returns>How many spots the trace was drawn with, which is most of what it cost.</returns>
    public static int Draw(DelayState memory, int across, int up, float[] into, float persistence)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(into);

        Array.Clear(into);
        if (into.Length < GlowStart + GlowSize * GlowSize) return 0;

        var rate = (double)memory.SampleRate;
        var lasts = Math.Max(persistence, 1e-4f) * rate;
        var span = (int)Math.Clamp(Math.Ceiling(lasts * Reach), 2, Math.Min(MaxSpan, DelayState.TraceSamples));

        var xs = ArrayPool<float>.Shared.Rent(span);
        var ys = ArrayPool<float>.Shared.Rent(span);

        try
        {
            memory.ReadTrace(across, xs.AsSpan(0, span));
            memory.ReadTrace(up, ys.AsSpan(0, span));

            return Trace(xs, ys, span, Math.Exp(-1d / lasts), into);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(xs);
            ArrayPool<float>.Shared.Return(ys);
        }
    }

    private static int Trace(float[] xs, float[] ys, int span, double fade, float[] into)
    {
        var trace = into.AsSpan(0, GlowStart);

        // A sound the beam cannot follow, noise above all, crosses the whole screen
        // every evaluation. Past a budget, every stride'th evaluation is drawn with
        // the weight of the ones skipped, which costs a picture of noise its detail
        // and nothing else.
        // Measured in texels, which are finite whatever was played.
        var travel = 0d;
        for (var i = 1; i < span; i++)
            travel += MathF.Abs(Texel(xs[i]) - Texel(xs[i - 1])) + MathF.Abs(Texel(ys[i]) - Texel(ys[i - 1]));

        var stride = (int)Math.Clamp(Math.Ceiling(travel / Budget), 1, span - 1);

        // Each evaluation's share of the window, newest largest, summing to one.
        var stretch = 1d - Math.Pow(fade, stride);
        var spot = new Spot();

        for (var i = stride + (span - 1 - stride) % stride; i < span; i += stride)
        {
            var share = (float)(Math.Pow(fade, span - 1 - i) * stretch);

            Walk(trace, ref spot, Texel(xs[i - stride]), Texel(ys[i - stride]), Texel(xs[i]), Texel(ys[i]), share);
        }

        spot.Flush(trace);

        Halo(trace, into.AsSpan(GlowStart, GlowSize * GlowSize));

        return spot.Drawn;
    }

    // From the picture's -1 to 1 to the trace's texels, centered on texel centers.
    private static float Texel(float value) =>
        float.IsFinite(value) ? Math.Clamp((value + 1f) * 0.5f * Size - 0.5f, -Size, 2 * Size) : -Size;

    // The beam crossing one segment at constant speed: its energy laid along it in
    // points no further apart than a spot is narrow, each an equal share.
    private static void Walk(Span<float> into, ref Spot spot, float ax, float ay, float bx, float by, float weight)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy) / Spacing));
        var share = weight / steps;

        for (var step = 0; step < steps; step++)
        {
            var along = (step + 0.5f) / steps;
            spot.Add(into, ax + dx * along, ay + dy * along, share);
        }
    }

    /// <summary>
    /// Where the beam has been since the last spot was drawn: points closer together
    /// than <see cref="Spacing"/> are one spot at their weighted middle, so a beam
    /// that dwells costs what one that moves does.
    /// </summary>
    private struct Spot
    {
        private float x, y, weight, sumX, sumY;

        /// <summary>How many spots have been drawn.</summary>
        public int Drawn { get; private set; }

        public void Add(Span<float> into, float px, float py, float share)
        {
            if (weight > 0f && (MathF.Abs(px - x) > Spacing || MathF.Abs(py - y) > Spacing)) Flush(into);

            if (weight == 0f)
            {
                x = px;
                y = py;
            }

            weight += share;
            sumX += px * share;
            sumY += py * share;
        }

        public void Flush(Span<float> into)
        {
            if (weight > 0f)
            {
                Splat(into, sumX / weight, sumY / weight, weight);
                Drawn++;
            }

            weight = sumX = sumY = 0f;
        }
    }

    // A Gaussian spot of energy, scaled so a line of them reads as its weight per
    // picture height of its length. Separable, and each axis's falloff is two
    // exponentials and a recurrence rather than one exponential a texel.
    private static void Splat(Span<float> into, float cx, float cy, float weight)
    {
        const float half = 0.5f / (Focus * Focus);
        const float energy = Size * 0.5f / (Focus * 2.5066283f);

        var left = (int)MathF.Floor(cx) - Radius + 1;
        var bottom = (int)MathF.Floor(cy) - Radius + 1;

        if (left + Width <= 1 || left >= Size - 1 || bottom + Width <= 1 || bottom >= Size - 1) return;

        Span<float> across = stackalloc float[Width];
        Span<float> up = stackalloc float[Width];

        Falloff(across, left - cx, half);
        Falloff(up, bottom - cy, half);

        var amount = weight * energy;

        for (var j = 0; j < Width; j++)
        {
            var row = bottom + j;
            if (row < 1 || row > Size - 2) continue;

            var line = into.Slice(row * Size, Size);
            var level = up[j] * amount;

            for (var i = 0; i < Width; i++)
            {
                var column = left + i;
                if (column < 1 || column > Size - 2) continue;

                line[column] += level * across[i];
            }
        }
    }

    // exp(-(d + k)^2 * half) for k from nought, by the ratio between neighbors.
    private static void Falloff(Span<float> into, float d, float half)
    {
        var value = MathF.Exp(-d * d * half);
        var ratio = MathF.Exp(-(2f * d + 1f) * half);
        var squeeze = MathF.Exp(-2f * half);

        for (var k = 0; k < into.Length; k++)
        {
            into[k] = value;
            value *= ratio;
            ratio *= squeeze;
        }
    }

    // The trace shrunk to the halo's size and blurred twice, so the halo is a few
    // texels of the trace wider than the trace in every direction.
    private static void Halo(ReadOnlySpan<float> trace, Span<float> glow)
    {
        const float share = 1f / (Shrink * Shrink);

        for (var row = 0; row < GlowSize; row++)
            for (var column = 0; column < GlowSize; column++)
            {
                var sum = 0f;

                for (var y = 0; y < Shrink; y++)
                    for (var x = 0; x < Shrink; x++)
                        sum += trace[(row * Shrink + y) * Size + column * Shrink + x];

                glow[row * GlowSize + column] = sum * share;
            }

        var scratch = ArrayPool<float>.Shared.Rent(glow.Length);

        for (var pass = 0; pass < 2; pass++)
        {
            Blur(glow, scratch, 1, GlowSize);
            Blur(scratch, glow, GlowSize, 1);
        }

        ArrayPool<float>.Shared.Return(scratch);

        // The edge is left black, as the trace's is, so a read off the side of the
        // halo is nothing rather than the edge held.
        for (var i = 0; i < GlowSize; i++)
        {
            glow[i] = glow[(GlowSize - 1) * GlowSize + i] = 0f;
            glow[i * GlowSize] = glow[i * GlowSize + GlowSize - 1] = 0f;
        }
    }

    // A binomial blur five texels wide, along one axis: step is how far a neighbor
    // is in the buffer, and stride how far the next row along the other axis is.
    private static void Blur(ReadOnlySpan<float> from, Span<float> into, int step, int stride)
    {
        for (var line = 0; line < GlowSize; line++)
            for (var i = 0; i < GlowSize; i++)
            {
                var sum = 0f;

                for (var k = -2; k <= 2; k++)
                {
                    var j = i + k;
                    if (j < 0 || j >= GlowSize) continue;

                    sum += from[line * stride + j * step] * Binomial[k + 2];
                }

                into[line * stride + i * step] = sum;
            }
    }

    private static ReadOnlySpan<float> Binomial => [1f / 16, 4f / 16, 6f / 16, 4f / 16, 1f / 16];
}
