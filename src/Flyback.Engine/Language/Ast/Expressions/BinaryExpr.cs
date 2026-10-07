namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>Infix arithmetic, which is the five binary maths modules by another spelling.</summary>
public sealed record BinaryExpr(TokenKind Operator, Expr Left, Expr Right, int Line, int Column)
    : Expr(Line, Column)
{
    public override bool Constant => Left.Constant && Right.Constant;

    public override bool Pipes => Left.Pipes || Right.Pipes;

    public override Expr Leftmost => Left.Leftmost;
}
