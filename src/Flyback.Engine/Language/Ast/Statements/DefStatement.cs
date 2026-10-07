namespace Flyback.Engine.Language.Ast.Statements;

/// <summary>
/// <c>def name(a, b = 1) = body</c>. Expanded at every call site and never compiled
/// as itself, so nothing about it survives into the patch.
/// </summary>
public sealed record DefStatement(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<Statement> Body,
    Expr? Result,
    IReadOnlyList<Expr>? Results,
    int Line,
    int Column) : Statement(Line, Column)
{
    public override string Naming => "def " + Name;
}
