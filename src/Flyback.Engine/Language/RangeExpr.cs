namespace Flyback.Core.Language;

/// <summary>
/// A low and a high written as one thing, which fills two sockets rather than
/// one — <c>remap(-2..2, 0..1)</c> is four arguments spelled as two.
/// </summary>
public sealed record RangeExpr(Expr Low, Expr High, int Line, int Column) : Expr(Line, Column);