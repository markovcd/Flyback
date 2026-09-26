namespace Flyback.Core.Language;

/// <summary>Infix arithmetic, which is the five binary maths modules by another spelling.</summary>
public sealed record BinaryExpr(TokenKind Operator, Expr Left, Expr Right, int Line, int Column)
    : Expr(Line, Column);