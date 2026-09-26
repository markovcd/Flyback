namespace Flyback.Core.Language;

/// <summary><c>let (a, b, c) = call</c>, which takes a def's several results apart.</summary>
public sealed record LetTupleStatement(
    IReadOnlyList<string> Names,
    Expr Value,
    int Line,
    int Column) : Statement(Line, Column);