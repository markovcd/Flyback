namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>(a, b)</c>: several values that travel together, such as one item of a list.</summary>
public sealed record TupleExpr(IReadOnlyList<Expr> Items, int Line, int Column) : Expr(Line, Column)
{
    public override bool Constant => Items.All(item => item.Constant);

    public override bool Pipes => Items.Any(item => item.Pipes);
}
