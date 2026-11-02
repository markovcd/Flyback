namespace Flyback.Engine.Language;

/// <summary>What may be written at one place in a patch's text.</summary>
/// <param name="Line">Counting from one, as an editor does.</param>
/// <param name="Column">Where the word being typed starts, counting from one; choosing an item replaces from here to the cursor.</param>
/// <param name="Typed">The part of the word already typed, which every item starts with.</param>
internal sealed record CompletionList(int Line, int Column, string Typed, IReadOnlyList<Completion> Items);
