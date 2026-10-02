namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>A leading minus, which is Negate.</summary>
public sealed record NegateExpr(Expr Value, int Line, int Column) : Expr(Line, Column);