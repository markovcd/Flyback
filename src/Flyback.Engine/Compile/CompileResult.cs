namespace Flyback.Core.Compile;

public sealed record CompileResult(CompiledPatch Program, IReadOnlyList<CompileIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;

    /// <summary>Whether anything here is wrong, as opposed to merely worth saying.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
}