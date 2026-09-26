namespace Flyback.Core.Language;

/// <summary>
/// <c>let name = pipeline</c>. The name reaches the finished patch as the
/// node's own label, so a patch built from text opens on the canvas already
/// named.
/// </summary>
public sealed record LetStatement(string Name, Expr Value, int Line, int Column)
    : Statement(Line, Column);