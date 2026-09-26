using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// A patch read from text, and everything wrong with the text it came from.
/// </summary>
/// <remarks>
/// Both halves are handed back, in the shape <see cref="PatchLoad"/> already
/// uses: a file with a mistake in it still describes most of a patch, and which
/// of those two facts matters is the caller's to decide. A batch job refuses;
/// an editor shows the complaints beside what it managed to build.
/// </remarks>
public sealed record LanguageLoad(Patch Patch, IReadOnlyList<LanguageIssue> Issues)
{
    /// <summary>What was read, kept so a complaint can show the line it is about.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Where the text says each module it built, and each knob it set.
    /// </summary>
    /// <remarks>
    /// What an editor needs and a batch job never asks for: the caret is in a
    /// module when the text at it is, and a knob turned in a panel goes back
    /// into the number the file already has for it.
    /// </remarks>
    public SourceMap Map { get; init; } = SourceMap.Empty;

    public bool Ok => Issues.Count == 0;

    /// <summary>
    /// Every complaint, each above the line it is about with the column marked.
    /// </summary>
    /// <remarks>
    /// The line is quoted rather than only numbered, and it earns the space: one
    /// mistake stops a statement being read, so a single stray comma comes back as four
    /// complaints, three about names that were never the problem. Whoever is reading
    /// has to see which one is the cause.
    /// </remarks>
    public string Report
    {
        get
        {
            if (Issues.Count == 0) return string.Empty;
            if (Source.Length == 0) return string.Join(Environment.NewLine, Issues);

            var lines = Source.ReplaceLineEndings("\n").Split('\n');
            var text = new System.Text.StringBuilder();

            foreach (var issue in Issues)
            {
                text.Append(issue.Line).Append(':').Append(issue.Column).Append(": ")
                    .AppendLine(issue.Message);

                if (issue.Line < 1 || issue.Line > lines.Length) continue;

                var line = lines[issue.Line - 1];

                text.Append("    ").AppendLine(line);
                text.Append("    ").Append(new string(' ', Math.Clamp(issue.Column - 1, 0, line.Length)))
                    .AppendLine("^");
            }

            return text.ToString().TrimEnd();
        }
    }
}