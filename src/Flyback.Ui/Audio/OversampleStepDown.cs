namespace Flyback.App.Audio;

/// <summary>
/// Whether the live sound should be worked out at a lower oversampling because it keeps
/// falling behind: 4× to 2× to 1×, one step at a time, and never back up in a run.
/// </summary>
/// <remarks>
/// Judged over <see cref="Window"/> of buffers: a quarter of them late, and at least
/// <see cref="FewestLate"/>, lowers the factor a step. After a step it waits
/// <see cref="Settle"/> before judging again, since a new rate starts with a new renderer.
/// Only a person raises it again, through Settings → Sound.
/// </remarks>
public sealed class OversampleStepDown
{
    /// <summary>How long a stretch of buffers is judged over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>How long a new factor plays before it is judged.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>The fewest late buffers in a window that count as falling behind, whatever their share.</summary>
    public const int FewestLate = 4;

    /// <summary>The share of a window's buffers that, late, count as falling behind.</summary>
    public const double LateShare = 0.25;

    /// <summary>The counts the buffers being judged are counted from, and whether there are any yet.</summary>
    private SoundTiming seen;
    private bool counting;

    /// <summary>When those buffers are judged: a window on, or after a step, once the new factor has settled.</summary>
    private TimeSpan due;

    /// <summary>
    /// Looks at the buffers timed since the last look, and answers the factor to play at
    /// now, or null to leave it where it is.
    /// </summary>
    /// <param name="now">Time since some fixed moment.</param>
    /// <param name="playing">Whether the sound is running; a stopped sound is not judged, and its buffers start a new window.</param>
    public int? Check(TimeSpan now, bool playing, SoundTiming timing, int oversample)
    {
        if (!playing || !counting)
        {
            Start(now, timing, Window);
            return null;
        }

        if (now < due) return null;

        var timed = timing.Timed - seen.Timed;
        var late = timing.Late - seen.Late;

        if (late < FewestLate || late < timed * LateShare || oversample <= 1)
        {
            Start(now, timing, Window);
            return null;
        }

        Start(now, timing, Settle);

        return oversample / 2;
    }

    /// <summary>Counts from <paramref name="now"/> on, judged once <paramref name="wait"/> has passed.</summary>
    private void Start(TimeSpan now, SoundTiming timing, TimeSpan wait)
    {
        counting = true;
        seen = timing;
        due = now + wait;
    }
}
