namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>
/// <c>for name in list { body }</c>: the body stamped out once per item, worth the
/// list of what each pass ends with (ADR-0208).
/// </summary>
/// <param name="Names">The name each item is bound to, or the names its tuple is taken apart into.</param>
/// <param name="Tupled">Whether the names were written in brackets, so each item is taken apart.</param>
/// <param name="Result">What a pass is worth, or null where the body ends in nothing.</param>
/// <param name="Results">What a pass is worth where it ends in several things.</param>
public sealed record ForExpr(
    IReadOnlyList<string> Names,
    bool Tupled,
    Expr Source,
    IReadOnlyList<Statement> Body,
    Expr? Result,
    IReadOnlyList<Expr>? Results,
    int Line,
    int Column) : Expr(Line, Column);
