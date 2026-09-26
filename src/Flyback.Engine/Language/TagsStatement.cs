namespace Flyback.Core.Language;

/// <summary><c>tags "..." "..."</c>: words to find the patch by, one string each.</summary>
public sealed record TagsStatement(IReadOnlyList<string> Tags, int Line, int Column) : Statement(Line, Column);