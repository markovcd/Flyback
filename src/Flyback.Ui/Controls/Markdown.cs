using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Flyback.Ui.Controls;

/// <summary>
/// As much Markdown as the changelog and the assistant's replies use, drawn as runs of one
/// text block: a <c>#</c> heading, a <c>-</c> or <c>*</c> bullet, a fenced block, and
/// <c>**bold**</c> and <c>`code`</c> inside a line.
/// </summary>
/// <remarks>
/// A marker left unpaired, as it is while a reply is still arriving, is shown as typed.
/// </remarks>
internal static class Markdown
{
    public static readonly FontFamily Code = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>The whole of <paramref name="text"/>, line by line.</summary>
    public static IReadOnlyList<Inline> Block(string text)
    {
        var inlines = new List<Inline>();
        var fenced = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (inlines.Count > 0) inlines.Add(new LineBreak());

            if (fenced)
            {
                if (line.Length > 0) inlines.Add(new Run(line) { FontFamily = Code });
                continue;
            }

            var indent = line.Length - line.TrimStart(' ').Length;
            var rest = line[indent..];

            if (Heading(rest) is { } heading)
            {
                foreach (var span in Spans(heading))
                {
                    span.FontWeight = FontWeight.SemiBold;
                    inlines.Add(span);
                }
            }
            else if (rest.StartsWith("- ", StringComparison.Ordinal) || rest.StartsWith("* ", StringComparison.Ordinal))
            {
                inlines.Add(new Run(new string(' ', indent) + "•  "));
                inlines.AddRange(Spans(rest[2..]));
            }
            else
            {
                inlines.AddRange(Spans(line));
            }
        }

        return inlines;
    }

    /// <summary>One line, with what is in backticks set in monospace and what is in double asterisks in bold.</summary>
    public static IReadOnlyList<Run> Spans(string line)
    {
        var runs = new List<Run>();
        var parts = Paired(line, "`");

        for (var index = 0; index < parts.Length; index++)
        {
            if (index % 2 == 1)
            {
                if (parts[index].Length > 0) runs.Add(new Run(parts[index]) { FontFamily = Code });
                continue;
            }

            var bold = Paired(parts[index], "**");

            for (var at = 0; at < bold.Length; at++)
            {
                if (bold[at].Length == 0) continue;

                var run = new Run(bold[at]);
                if (at % 2 == 1) run.FontWeight = FontWeight.SemiBold;
                runs.Add(run);
            }
        }

        return runs;
    }

    /// <summary>The text of a <c>#</c> to <c>######</c> heading, or null when the line is none.</summary>
    private static string? Heading(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;

        return level is > 0 and <= 6 && line.Length > level && line[level] == ' ' ? line[(level + 1)..] : null;
    }

    /// <summary>
    /// <paramref name="text"/> split on <paramref name="marker"/>, odd pieces inside a pair;
    /// a stray marker leaves the line whole rather than marking the rest of it.
    /// </summary>
    private static string[] Paired(string text, string marker)
    {
        var parts = text.Split(marker);
        return parts.Length % 2 == 1 ? parts : [text];
    }
}
