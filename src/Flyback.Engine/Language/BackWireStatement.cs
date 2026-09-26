namespace Flyback.Core.Language;

/// <summary>
/// <c>name.port &lt;- pipeline</c>, which is how a cycle is closed: the one wire
/// that cannot be written as a pipeline because it runs backwards.
/// </summary>
public sealed record BackWireStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);