namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>A number as it was written, and how it was written.</summary>
public sealed record NumberExpr(double Value, NumberStyle Style, int Line, int Column)
    : Expr(Line, Column)
{
    public override bool Constant => true;
}
