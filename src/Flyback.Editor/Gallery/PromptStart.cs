namespace Flyback.Editor.Gallery;

/// <summary>
/// What the gallery needs to offer a card to start from a prompt: a way to write a
/// short idea out as a detailed brief, answering the brief or why there is none.
/// </summary>
internal sealed record PromptStart(Func<string, CancellationToken, Task<(string? Brief, string? Failure)>> Expand);
