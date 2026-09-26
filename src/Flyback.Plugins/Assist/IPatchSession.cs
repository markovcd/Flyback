namespace Flyback.Plugins.Assist;

/// <summary>
/// A conversation in progress. Multi-turn on purpose: the second instruction —
/// "more blue, and slower" — is the common one, and it should keep both the
/// history and whatever prompt cache the provider built for the first.
/// </summary>
public interface IPatchSession : IDisposable
{
    /// <summary>
    /// One turn. Yields as work happens and completes when the assistant stops.
    /// </summary>
    /// <remarks>
    /// Never throws for a provider failure — that is a
    /// <see cref="PatchEvent.Failed"/>, so a bad key or a dropped connection
    /// costs the turn rather than the window. Cancellation ends the sequence and
    /// leaves the workbench wherever it got to, which is safe because the
    /// workbench is a copy.
    /// </remarks>
    IAsyncEnumerable<PatchEvent> Ask(string instruction, CancellationToken cancel);

    /// <summary>
    /// The conversation so far, in whatever shape <see cref="IPatchAssistant.Resume"/>
    /// needs to be handed it back, or null where it cannot be kept.
    /// </summary>
    /// <remarks>
    /// Asked between turns, never during one. It goes into the file the person
    /// saves, and a bundle is what they send to other people, so it must never
    /// hold the key or anything that needs one. Pictures and clips are better
    /// left out: they are nearly all of the size, and the model can render or
    /// listen again.
    /// </remarks>
    string? Save() => null;
}