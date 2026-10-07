using Flyback.Core.Compile;

namespace Flyback.Engine.Language;

/// <summary>What the text got wrong, said where it says it.</summary>
internal sealed class Issues(List<LanguageIssue> list)
{
    /// <summary>The list itself, for a reader that adds to it on its own.</summary>
    public List<LanguageIssue> List => list;

    public int Count => list.Count;

    public void Complain(string code, int line, int column, string message) =>
        list.Add(new LanguageIssue(line, column, code, message));

    /// <summary>Said, but not held against the text: the patch is built all the same.</summary>
    public void Warn(string code, int line, int column, string message) =>
        list.Add(new LanguageIssue(line, column, code, message, IssueSeverity.Warning));
}
