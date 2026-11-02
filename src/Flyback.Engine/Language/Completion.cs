namespace Flyback.Engine.Language;

/// <summary>One word an editor may offer where the cursor is.</summary>
/// <param name="Label">The word as the text writes it.</param>
/// <param name="Detail">One line on what it is, for beside the list.</param>
/// <param name="Insert">What choosing it writes in place of the word typed so far.</param>
internal sealed record Completion(string Label, CompletionKind Kind, string Detail, string Insert);
