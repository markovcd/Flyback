namespace Flyback.Core.Language;

/// <summary>The one string the language has, which names a file.</summary>
public sealed record TextExpr(string Value, int Line, int Column) : Expr(Line, Column);