namespace Flyback.Core.Language;

/// <summary><c>name.port = value</c>, which turns a knob.</summary>
public sealed record KnobStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);