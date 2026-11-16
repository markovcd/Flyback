namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>list[i]</c>: one item of a list, counted from one, read as the text is built.</summary>
public sealed record IndexExpr(Expr Source, Expr Position, int Line, int Column) : Expr(Line, Column)
{
    public override bool Pipes => Source.Pipes || Position.Pipes;

    public override Expr Leftmost => Source.Leftmost;
}
