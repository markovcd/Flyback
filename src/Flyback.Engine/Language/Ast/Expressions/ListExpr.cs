namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>[a, b, c]</c>: values written down to be looped over, spread or added up.</summary>
public sealed record ListExpr(IReadOnlyList<Expr> Items, int Line, int Column) : Expr(Line, Column)
{
    public override bool Constant => Items.All(item => item.Constant);

    public override bool Pipes => Items.Any(item => item.Pipes);
}
