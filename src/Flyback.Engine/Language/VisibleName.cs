namespace Flyback.Engine.Language;

/// <summary>A name the text bound that is in sight at some place in it.</summary>
/// <param name="Line">The line it is bound on.</param>
/// <param name="Callee">The module or def a <c>let</c> placed, as written, where it placed one call; null otherwise.</param>
/// <param name="Parameters">A def's parameters, in order; empty for anything else.</param>
internal sealed record VisibleName(string Name, CompletionKind Kind, int Line, string? Callee, IReadOnlyList<string> Parameters);
