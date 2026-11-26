namespace Flyback.Engine.Language.Ast.Statements;

/// <summary>
/// <c>def name(a, b = 1) = body</c>. Expanded at every call site and never compiled
/// as itself, so nothing about it survives into the patch. Written <c>module</c>, it is
/// also one a pack offers.
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
    /// <summary>Whether it was written <c>module</c>: a def its pack offers by id.</summary>
    public bool Offered { get; init; }

    /// <summary>The <c>out</c> lines a module's body ends with, in order; empty where it ends with a plain result.</summary>
    public IReadOnlyList<NamedOutput> Named { get; init; } = [];

    public override string Naming => (Offered ? "module " : "def ") + Name;
}
