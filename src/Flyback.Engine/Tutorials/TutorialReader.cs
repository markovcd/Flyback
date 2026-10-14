using System.Text;

namespace Flyback.Engine.Tutorials;

/// <summary>
/// Reads a tutorial file: a <c># title</c>, a <c>From:</c> line naming the preset it opens on, a
/// summary, then a <c>## heading</c> for each step, its text, and at most one <c>```fbks</c> block
/// holding the step's goal.
/// </summary>
internal static class TutorialReader
{
    private const string Fence = "```";
    private const string GoalFence = "```fbks";
    private const string FromLine = "From:";

    /// <exception cref="FormatException">The file is not shaped as above; only shipped files are read.</exception>
    public static Tutorial Read(string id, string text)
    {
        string? title = null, from = null;
        var summary = new StringBuilder();
        var steps = new List<TutorialStep>();
        string? heading = null;
        var body = new StringBuilder();
        StringBuilder? goal = null;
        var inGoal = false;

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (inGoal)
            {
                if (line == Fence) inGoal = false;
                else goal!.AppendLine(line);
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                heading = line[3..].Trim();
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal) && title is null && heading is null)
            {
                title = line[2..].Trim();
            }
            else if (heading is null && line.StartsWith(FromLine, StringComparison.Ordinal))
            {
                from = line[FromLine.Length..].Trim();
            }
            else if (line == GoalFence)
            {
                if (heading is null) throw Wrong("a goal before the first step");
                if (goal is not null) throw Wrong($"two goals in '{heading}'");
                goal = new StringBuilder();
                inGoal = true;
            }
            else if (heading is null)
            {
                Paragraph(summary, line);
            }
            else
            {
                Paragraph(body, line);
            }
        }

        if (inGoal) throw Wrong($"an unclosed goal in '{heading}'");
        Close();

        if (title is null) throw Wrong("no '# title'");
        if (from is null) throw Wrong("no 'From:' line");
        if (steps.Count == 0) throw Wrong("no steps");

        return new Tutorial(id, title, summary.ToString().Trim(), from, steps);

        void Close()
        {
            if (heading is null) return;
            var written = goal?.ToString().Trim();
            steps.Add(new TutorialStep(heading, body.ToString().Trim(), string.IsNullOrEmpty(written) ? null : written));
            body.Clear();
            goal = null;
        }

        FormatException Wrong(string what) => new($"Tutorial '{id}' has {what}.");
    }

    /// <summary>Joins wrapped lines into one paragraph and keeps a blank line between paragraphs.</summary>
    private static void Paragraph(StringBuilder into, string line)
    {
        if (line.Length == 0)
        {
            if (into.Length > 0 && !EndsWithBreak(into)) into.Append("\n\n");
            return;
        }

        if (into.Length > 0 && !EndsWithBreak(into)) into.Append(' ');
        into.Append(line.Trim());
    }

    private static bool EndsWithBreak(StringBuilder text) => text.Length > 0 && text[^1] == '\n';
}
