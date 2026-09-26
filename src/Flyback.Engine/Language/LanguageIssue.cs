namespace Flyback.Core.Language;

/// <summary>
/// Something wrong with a source file, said where it is. A value rather than an
/// exception, for the reason every other boundary in the engine reports that way: a
/// file with four mistakes should say all four, and a parser that throws can only say
/// the first.
/// </summary>
/// <param name="Line">Counting from one, as an editor does.</param>
/// <param name="Column">Counting from one, as an editor does.</param>
/// <param name="Code">One of <see cref="IssueCode"/>.</param>
public sealed record LanguageIssue(int Line, int Column, string Code, string Message)
{
    public override string ToString() => $"{Line}:{Column}: {Message}";
}