namespace Flyback.Core.Language;

/// <summary>
/// <c>description "..."</c>: what the patch is for, in a line of prose, which may
/// run on as further strings on the lines below.
/// </summary>
public sealed record DescriptionStatement(string Text, int Line, int Column) : Statement(Line, Column);