namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>a and b</c> or <c>a or b</c>, which only an <c>if</c>'s condition reads.</summary>
/// <param name="And">Whether both must hold, rather than either.</param>
public sealed record LogicExpr(bool And, Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column)
{
    public override bool Pipes => Left.Pipes || Right.Pipes;

    public override Expr Leftmost => Left.Leftmost;
}
