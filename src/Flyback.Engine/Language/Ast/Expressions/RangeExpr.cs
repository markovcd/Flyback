namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>
/// A low and a high written as one thing, which fills two sockets rather than
/// one — <c>remap(-2..2, 0..1)</c> is four arguments spelled as two.
/// </summary>
public sealed record RangeExpr(Expr Low, Expr High, int Line, int Column) : Expr(Line, Column)
{
    public override bool Pipes => Low.Pipes || High.Pipes;

    public override Expr Leftmost => Low.Leftmost;
}
