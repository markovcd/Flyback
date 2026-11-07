using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Engine.Language;

/// <summary>
/// The Song's block: each part its name and a colon, then its levels, written as an
/// Arrangement's are. The next name starts the next part.
/// </summary>
internal static class SongNotation
{
    /// <summary>Sections the printer puts on one line before it carries a part on to the next.</summary>
    private const int LineSections = 16;

    /// <summary>The parts <paramref name="block"/> writes.</summary>
    /// <param name="line">The line the block's '[' is on.</param>
    /// <param name="column">The column the block's '[' is in.</param>
    public static List<SongPart> Read(string block, int line, int column, List<LanguageIssue> issues)
    {
        var parts = new List<(string Name, List<PartLevel> Levels)>();
        var said = issues.Count;

        for (var at = 0; at < block.Length;)
        {
            if (char.IsWhiteSpace(block[at]) || block[at] == ',')
            {
                at++;
                continue;
            }

            var start = at;
            while (at < block.Length && !char.IsWhiteSpace(block[at]) && block[at] is not (',' or ':')) at++;

            var word = block[start..at];
            var (wordLine, wordColumn) = StepNotation.Where(block, start, line, column);

            if (at < block.Length && block[at] == ':')
            {
                at++;

                if (!SongExtra.Writable(word))
                {
                    issues.Add(new LanguageIssue(wordLine, wordColumn, IssueCode.PartName,
                        $"'{word}' is not a part's name: start it with a letter, then letters, digits and '_'."));
                }
                else if (parts.Any(part => part.Name.Equals(word, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add(new LanguageIssue(wordLine, wordColumn, IssueCode.PartName, $"the Song already has a part called '{word}'."));
                }

                parts.Add((word, []));
                continue;
            }

            if (parts.Count == 0)
            {
                issues.Add(new LanguageIssue(wordLine, wordColumn, IssueCode.PartName,
                    $"a Song's part starts with its name and a colon, as in 'kick: {word}'."));

                return [];
            }

            if (ArrangementNotation.Level(word) is { } level)
            {
                parts[^1].Levels.Add(level);
                continue;
            }

            issues.Add(new LanguageIssue(wordLine, wordColumn, IssueCode.LevelSyntax,
                $"'{word}' is not a level: write a number, with '>' before one that glides."));
        }

        if (parts.Count > NodeCatalog.MaxParts)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.TooManyParts,
                $"this block writes {parts.Count} parts, and the Song holds at most {NodeCatalog.MaxParts}."));

            return [];
        }

        if (parts.FirstOrDefault(part => part.Levels.Count > SongModule.MaxSections) is { Name: not null } longest)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.TooManyParts,
                $"'{longest.Name}' has {longest.Levels.Count} sections, and a part holds at most {SongModule.MaxSections}."));

            return [];
        }

        if (parts.FirstOrDefault(part => part.Levels.Count == 0) is { Name: not null } empty)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.LevelSyntax, $"'{empty.Name}' has no levels: write one a section after its name."));
        }

        return issues.Count > said ? [] : [.. parts.Select(part => new SongPart(part.Name, part.Levels))];
    }

    /// <summary>The block that writes <paramref name="parts"/>: a part a line, names lined up, a long part carried on.</summary>
    public static string? Write(IReadOnlyList<SongPart> parts)
    {
        if (parts.Count == 0) return null;

        var width = parts.Max(part => part.Name.Length) + 1;
        var lines = new List<string> { "[" };

        foreach (var part in parts)
        {
            for (var from = 0; from < part.Levels.Count; from += LineSections)
            {
                var head = from == 0 ? (part.Name + ":").PadRight(width) : new string(' ', width);
                lines.Add("    " + head + " " + ArrangementNotation.Row(part.Levels.Skip(from).Take(LineSections)));
            }
        }

        lines.Add("]");
        return string.Join('\n', lines);
    }
}
