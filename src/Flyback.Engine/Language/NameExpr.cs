namespace Flyback.Core.Language;

/// <summary>
/// A name, and optionally one of its outputs: <c>riff</c>, <c>riff.gate</c>,
/// <c>out.color</c>.
/// </summary>
public sealed record NameExpr(string Name, string? Port, int Line, int Column) : Expr(Line, Column);