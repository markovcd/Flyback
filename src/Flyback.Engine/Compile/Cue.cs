namespace Flyback.Core.Compile;

/// <summary>
/// When a patch just opened starts playing: its sound and its picture wait on one
/// cue, which goes once everything that took a part of it has given it back. See
/// ADR-0076.
/// </summary>
/// <remarks>
/// Made holding one part, the opener's own, so nothing can finish before
/// everything has had the chance to take a part; <see cref="Give"/> it once they
/// have. Past <see cref="Patience"/> it goes regardless, so a part nobody gives
/// back (a preview that is never drawn) costs a wait rather than the patch.
/// </remarks>
public sealed class Cue
{
    /// <summary>The longest a cue waits: several times the slowest shader measured.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly long deadline;
    private int parts = 1;

    public Cue(TimeSpan? patience = null) =>
        deadline = Environment.TickCount64 + (long)(patience ?? Patience).TotalMilliseconds;

    /// <summary>Whether whatever waits on this is still waiting. Cheap enough for the audio callback.</summary>
    public bool Waiting => Volatile.Read(ref parts) > 0 && Environment.TickCount64 < deadline;

    /// <summary>One more thing to finish before this goes.</summary>
    public void Take() => Interlocked.Increment(ref parts);

    /// <summary>One thing finished. Each <see cref="Take"/>, and the opener's own part, is given back once.</summary>
    public void Give() => Interlocked.Decrement(ref parts);
}
