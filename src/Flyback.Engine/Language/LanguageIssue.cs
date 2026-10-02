using Flyback.Core.Compile;

namespace Flyback.Engine.Language;

/// <summary>
/// Something wrong with a source file, said where it is. A value rather than an
/// exception, for the reason every other boundary in the engine reports that way: a
/// file with four mistakes should say all four, and a parser that throws can only say
/// the first.
/// </summary>
/// <param name="Line">Counting from one, as an editor does.</param>
/// <param name="Column">Counting from one, as an editor does.</param>
/// <param name="Code">One of <see cref="IssueCode"/>.</param>
/// <param name="Severity">A warning is said and the patch still builds.</param>
public sealed record LanguageIssue(int Line, int Column, string Code, string Message, IssueSeverity Severity = IssueSeverity.Error)
{
    public bool IsError => Severity == IssueSeverity.Error;

    public override string ToString() => IsError ? $"{Line}:{Column}: {Message}" : $"{Line}:{Column}: warning: {Message}";
}
