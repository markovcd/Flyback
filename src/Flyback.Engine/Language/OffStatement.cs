namespace Flyback.Core.Language;

/// <summary>
/// <c>off name</c>, which takes a module out of the signal path: what is
/// patched into it comes out of it, and nothing does where nothing is.
/// </summary>
public sealed record OffStatement(NameExpr Target, int Line, int Column) : Statement(Line, Column);