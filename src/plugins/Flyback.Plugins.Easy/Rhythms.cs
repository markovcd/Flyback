using Flyback.Core.Compile;

namespace Flyback.Plugins.Easy;

/// <summary>
/// The Easy Drum's rhythms: which of a bar's sixteen sixteenths it plays on, and
/// how long ago it last played, read off the clock.
/// </summary>
/// <remarks>
/// Stateless, like the Stroke: the time since the last hit is arithmetic on the
/// domain, so every drum at one tempo is on one grid, it cannot drift, and the
/// screen sees the same hits the speakers hear.
/// </remarks>
internal static class Rhythms
{
    public const string Auto = "auto";
    public const string Beats = "beats";
    public const string Backbeat = "backbeat";
    public const string Eighths = "eighths";
    public const string Sixteenths = "sixteenths";
    public const string Offbeats = "offbeats";
    public const string Tresillo = "tresillo";
    public const string Clave = "clave";
    public const string Bar = "bar";
    public const string Trigger = "trigger";

    private const int Steps = 16;

    /// <summary>How late a full 'swing' plays each second sixteenth, as a share of the pair it ends.</summary>
    private const float LatestSwing = 0.25f;

    private static readonly Dictionary<string, int[]> Hits = new(StringComparer.Ordinal)
    {
        [Beats] = [0, 4, 8, 12],
        [Backbeat] = [4, 12],
        [Eighths] = [0, 2, 4, 6, 8, 10, 12, 14],
        [Sixteenths] = [.. Enumerable.Range(0, Steps)],
        [Offbeats] = [2, 6, 10, 14],
        [Tresillo] = [0, 3, 6, 8, 11, 14],
        [Clave] = [0, 3, 6, 10, 12],
        [Bar] = [0],
    };

    /// <summary>The rhythm a sound plays when it is left on Auto: the one it is usually heard in.</summary>
    public static string For(string sound) => sound switch
    {
        Kit.Snare or Kit.Clap => Backbeat,
        Kit.ClosedHat => Eighths,
        Kit.OpenHat => Offbeats,
        Kit.Tom => Tresillo,
        Kit.Rim => Clave,
        Kit.Cowbell => Offbeats,
        _ => Beats,
    };

    /// <summary>
    /// Seconds since <paramref name="rhythm"/> last played at <paramref name="bpm"/>, and
    /// 1 once it has played at all.
    /// </summary>
    public static (Slot Age, Slot Started) Age(Emitter em, Slot domain, Slot bpm, Slot swing, string rhythm)
    {
        var hits = Hits.GetValueOrDefault(rhythm, Hits[Beats]);

        var position = Swung(em, em.Mul(em.Mul(domain, bpm), 1f / 15f), swing);
        var whole = em.Unary(OpCode.Floor, position);
        var index = em.Binary(OpCode.Mod, whole, em.Constant(Steps));

        // Steps since the last hit, for each step of the bar, picked by the one playing.
        var since = em.Constant(Since(hits, 0));
        for (var step = 1; step < Steps; step++)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (Since(hits, step) == Since(hits, step - 1)) continue;

            since = em.Ternary(
                OpCode.Mix,
                since,
                em.Constant(Since(hits, step)),
                em.Binary(OpCode.Step, em.Constant(step - 0.5f), index));
        }

        var steps = em.Add(since, em.Unary(OpCode.Fract, position));
        var started = em.Binary(OpCode.Step, em.Constant(0f), em.Sub(whole, since));

        return (em.Binary(OpCode.Div, em.Mul(steps, 15f), bpm), started);
    }

    /// <summary>Every second sixteenth played later by the swing, the pair's first stretched to make room.</summary>
    private static Slot Swung(Emitter em, Slot sixteenths, Slot swing)
    {
        var pairs = em.Mul(sixteenths, 0.5f);
        var pair = em.Unary(OpCode.Floor, pairs);
        var within = em.Unary(OpCode.Fract, pairs);

        var split = em.Add(em.Mul(swing, LatestSwing), 0.5f);
        var first = em.Binary(OpCode.Div, em.Mul(within, 0.5f), split);
        var second = em.Add(
            em.Binary(OpCode.Div, em.Mul(em.Sub(within, split), 0.5f), em.Sub(em.Constant(1f), split)),
            0.5f);

        var warped = em.Ternary(OpCode.Mix, first, second, em.Binary(OpCode.Step, split, within));

        return em.Mul(em.Add(pair, warped), 2f);
    }

    /// <summary>Steps from the last hit at or before <paramref name="step"/>, round the bar if need be.</summary>
    private static float Since(int[] hits, int step)
    {
        var last = hits.Where(hit => hit <= step).DefaultIfEmpty(hits.Max() - Steps).Max();

        return step - last;
    }
}
