namespace Flyback.Plugins.Assist;

/// <summary>Answers keyed by the question identifiers from a request.</summary>
public sealed class LayaResponse(
    IReadOnlyDictionary<string, LayaAnswer> answers,
    string checkpoint,
    string? revision)
{
    /// <summary>Answers keyed by question identifier.</summary>
    public IReadOnlyDictionary<string, LayaAnswer> Answers { get; } = answers;

    /// <summary>The model checkpoint that produced these answers.</summary>
    public string Checkpoint { get; } = checkpoint;

    /// <summary>The resolved checkpoint revision, when available.</summary>
    public string? Revision { get; } = revision;
}
