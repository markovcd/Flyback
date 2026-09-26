namespace Flyback.Core.Language;

/// <summary>
/// <c>def name(a, b) = body</c>. Expanded at every call site and never compiled
/// as itself, so nothing about it survives into the patch.
/// </summary>
public sealed record DefStatement(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Statement> Body,
    Expr? Result,
    IReadOnlyList<Expr>? Results,
    int Line,
    int Column) : Statement(Line, Column);