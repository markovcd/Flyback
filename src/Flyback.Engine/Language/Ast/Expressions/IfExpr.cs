namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>
/// <c>if condition { ... } else { ... }</c>: one branch built, chosen as the text is
/// built from what the text writes down (ADR-0234).
/// </summary>
/// <param name="Otherwise">The <c>else</c> branch, which holds the next <c>if</c> of an <c>else if</c>; null where there is none.</param>
public sealed record IfExpr(Expr Condition, Branch Then, Branch? Otherwise, int Line, int Column) : Expr(Line, Column);
