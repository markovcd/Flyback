using Flyback.Engine.Render;

namespace Flyback.Viewer.Web;

/// <summary>
/// Whether the worker's sound keeps pace: each chunk timed against how long it plays for,
/// and judged by <see cref="OversampleStepDown"/> as the desktop judges its buffers.
/// </summary>
internal sealed class SoundPace
{
    private readonly OversampleStepDown judge = new();
    private long timed;
    private long late;

    /// <summary>Whether the sound was made anew since the last look, so starts cold.</summary>
    private bool renewed;

    public SoundTiming Timing => new(timed, late);

    /// <summary>Whether the sound has fallen behind at 1×, with no lower factor left to step down to.</summary>
    public bool Behind { get; private set; }

    /// <summary>Counts a chunk that took <paramref name="took"/> to render and plays for <paramref name="plays"/>.</summary>
    public void Count(TimeSpan took, TimeSpan plays)
    {
        timed++;
        if (took > plays) late++;
    }

    /// <summary>The sound was made anew, as a new script whose first chunks run cold, so it is left to settle.</summary>
    public void Renew() => renewed = true;

    /// <summary>Looks at the chunks counted since the last look, and answers the factor to play at now, or null to leave it.</summary>
    /// <inheritdoc cref="OversampleStepDown.Check" path="/param"/>
    public int? Judge(TimeSpan now, bool playing, int oversample)
    {
        if (renewed)
        {
            renewed = false;
            judge.Renewed(now);
        }

        var verdict = judge.Check(now, playing, Timing, oversample);
        if (verdict.Behind) Behind = true;

        return verdict.Lower;
    }
}
