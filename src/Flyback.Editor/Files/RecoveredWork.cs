namespace Flyback.Editor.Files;

/// <summary>
/// Unsaved work as it stood a moment ago, written where the next start can find it
/// should this one never get as far as asking whether to save.
/// </summary>
/// <param name="Name">What the title bar called it, or null for a document with no name.</param>
/// <param name="Beside">The folder its sounds and pictures were measured from, or null.</param>
/// <param name="Patch">The patch on the canvas, as <see cref="Core.Graph.PatchIO.ToJson"/> writes it.</param>
/// <param name="Source">The text, where the text was the document (ADR-0068), and null where it was not.</param>
/// <param name="Conversation">The conversation about it, as the assistant saves one (ADR-0072).</param>
/// <param name="Files">What an open bundle was carrying, and null for a document that was not one.</param>
public sealed record RecoveredWork(
    string? Name,
    string? Beside,
    string Patch,
    string? Source,
    string? Conversation,
    IReadOnlyDictionary<string, byte[]>? Files);