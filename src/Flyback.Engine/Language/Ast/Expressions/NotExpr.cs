namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary><c>not a</c>, which only an <c>if</c>'s condition reads.</summary>
public sealed record NotExpr(Expr Operand, int Line, int Column) : Expr(Line, Column)
{
    public override bool Pipes => Operand.Pipes;
}
