namespace Flyback.Plugins.Assist;

/// <summary>
/// A session whose turns the host runs: the provider says only how the conversation
/// is written down in its format, and sent (ADR-0161).
/// </summary>
/// <remarks>
/// The host runs <see cref="TurnLoop"/> over one of these itself, so what a turn
/// promises the panel holds whatever the provider does. <see cref="IPatchSession.Ask"/>
/// is answered here, for a caller that asks anyway; a provider whose conversation is
/// some other shape implements <see cref="IPatchSession"/> and writes its own.
/// </remarks>
public interface IModelConversation : IPatchSession
{
    /// <summary>The patch being built, as <see cref="IPatchAssistant.Start"/> was handed it.</summary>
    PatchWorkbench Workbench { get; }

    /// <summary>
    /// Whether the model doing the building takes a sound, so a clip goes back with
    /// the answers rather than being described by <see cref="EarModel"/>.
    /// </summary>
    bool HearsItself { get; }

    /// <summary>The model that listens for one that cannot, or null where none was chosen.</summary>
    string? EarModel { get; }

    /// <summary>Replaces every picture and clip already in the conversation with a line saying one was there.</summary>
    void Forget();

    /// <summary>Adds the person's message.</summary>
    void Add(string instruction);

    /// <summary>Sends the conversation and adds what came back to it. Throws what the endpoint refused with.</summary>
    Task<ModelReply> Send(CancellationToken cancel);

    /// <summary>Adds the answers to the last reply's calls, one each and in its order, with their pictures and clips.</summary>
    void Add(IReadOnlyList<ToolAnswer> answers);

    /// <summary>
    /// Plays <paramref name="wav"/> to <paramref name="model"/>, told
    /// <paramref name="briefing"/> first, in a request of its own that never joins
    /// the conversation, and says what it said.
    /// </summary>
    Task<string?> Listen(string model, string briefing, byte[] wav, CancellationToken cancel);

    IAsyncEnumerable<PatchEvent> IPatchSession.Ask(string instruction, CancellationToken cancel) =>
        TurnLoop.Run(this, instruction, cancel);
}
