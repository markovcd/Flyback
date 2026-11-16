namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>a == b</c>, <c>a &lt; b</c> and the rest, which only an <c>if</c>'s condition reads.</summary>
public sealed record CompareExpr(TokenKind Operator, Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column)
{
    public override bool Pipes => Left.Pipes || Right.Pipes;

    public override Expr Leftmost => Left.Leftmost;
}
