namespace Flyback.Core.Render;

/// <summary>
/// Whether the live sound should be worked out at a lower oversampling because it keeps
/// falling behind: 4× to 2× to 1×, one step at a time, and never back up in a run.
/// </summary>
/// <remarks>
/// Judged over <see cref="Window"/> of buffers: a quarter of them late, and at least
/// <see cref="FewestLate"/>, lowers the factor a step, or at 1× says the sound is behind.
/// After a step it waits <see cref="Settle"/> before judging again, since a new rate starts
/// with a new renderer. A sound made anew by <see cref="Renewed"/> has nothing counted until
/// it has settled. Only a person raises the factor again, through Settings → Sound.
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

    /// <summary>Until when the buffers of a sound made anew count for nothing.</summary>
    private TimeSpan settled;

    /// <summary>
    /// Looks at the buffers timed since the last look, and answers the factor to play at
    /// now, or that the sound is behind with none lower to go to.
    /// </summary>
    /// <param name="now">Time since some fixed moment.</param>
    /// <param name="playing">Whether the sound is running; a stopped sound is not judged, and its buffers start a new window.</param>
    public StepDownVerdict Check(TimeSpan now, bool playing, SoundTiming timing, int oversample)
    {
        if (!playing || !counting || now < settled)
        {
            Start(now, timing, Window);
            return StepDownVerdict.Keep;
        }

        if (now < due) return StepDownVerdict.Keep;

        var timed = timing.Timed - seen.Timed;
        var late = timing.Late - seen.Late;

        if (late < FewestLate || late < timed * LateShare)
        {
            Start(now, timing, Window);
            return StepDownVerdict.Keep;
        }

        if (oversample <= 1)
        {
            Start(now, timing, Window);
            return new StepDownVerdict(null, Behind: true);
        }

        Start(now, timing, Settle);

        return new StepDownVerdict(oversample / 2, Behind: false);
    }

    /// <summary>
    /// The sound was made anew at <paramref name="now"/>, as a new script that runs slow
    /// until the engine has warmed to it: its buffers count once <see cref="Settle"/> has passed.
    /// </summary>
    public void Renewed(TimeSpan now) => settled = now + Settle;

    /// <summary>Counts from <paramref name="now"/> on, judged once <paramref name="wait"/> has passed.</summary>
    private void Start(TimeSpan now, SoundTiming timing, TimeSpan wait)
    {
        counting = true;
        seen = timing;
        due = now + wait;
    }
}
