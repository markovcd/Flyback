using System.Globalization;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Language;

/// <summary>
/// An Arrangement's block: a row of levels for each part, the parts separated by
/// '|'. A level is a number, '~' for nought, and '>' in front of one that glides.
/// </summary>
/// <remarks>
/// Only '|' ends a part, so a long part may run over as many lines as it likes, the
/// way a long tune does.
/// </remarks>
internal static class ArrangementNotation
{
    /// <summary>The parts <paramref name="block"/> writes, one list of levels each.</summary>
    /// <param name="line">The line the block's '[' is on.</param>
    /// <param name="column">The column the block's '[' is in.</param>
    public static List<List<PartLevel>> Read(string block, int line, int column, List<LanguageIssue> issues)
    {
        var parts = new List<List<PartLevel>> { new() };

        for (var at = 0; at < block.Length;)
        {
            var c = block[at];

            if (char.IsWhiteSpace(c) || c == ',')
            {
                at++;
                continue;
            }

            if (c == '|')
            {
                parts.Add([]);
                at++;
                continue;
            }

            var start = at;
            while (at < block.Length && !char.IsWhiteSpace(block[at]) && block[at] is not (',' or '|')) at++;

            if (Level(block[start..at]) is { } level)
            {
                parts[^1].Add(level);
                continue;
            }

            var (wordLine, wordColumn) = StepNotation.Where(block, start, line, column);

            issues.Add(new LanguageIssue(
                wordLine, wordColumn, IssueCode.LevelSyntax,
                $"'{block[start..at]}' is not a level: write a number, '~' for nought, or '>' before a number that glides."));
        }

        parts.RemoveAll(part => part.Count == 0);

        if (parts.Count > NodeCatalog.MaxParts)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.TooManyParts,
                $"this block writes {parts.Count} parts, and an Arrangement holds at most {NodeCatalog.MaxParts}."));

            return [];
        }

        if (parts.FirstOrDefault(part => part.Count > NodeCatalog.MaxSections) is { } longest)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.TooManyParts,
                $"part {parts.IndexOf(longest) + 1} has {longest.Count} sections, and an Arrangement holds at most {NodeCatalog.MaxSections}."));

            return [];
        }

        return parts;
    }

    /// <summary>The block that writes <paramref name="parts"/>, or null where there are none.</summary>
    public static string? Write(IReadOnlyList<IReadOnlyList<PartLevel>> parts) =>
        parts.Count == 0 ? null : "[ " + string.Join(" | ", parts.Select(Row)) + " ]";

    /// <summary>One part's levels as the block writes them, with no brackets.</summary>
    public static string Row(IEnumerable<PartLevel> part) => string.Join(' ', part.Select(Written));

    private static PartLevel? Level(string word)
    {
        var glides = word.StartsWith('>');
        var number = glides ? word[1..] : word;

        if (number == "~") return new PartLevel(0f, glides);

        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && double.IsFinite(value)
            ? new PartLevel((float)value, glides)
            : null;
    }

    private static string Written(PartLevel level) =>
        (level.Glides ? ">" : "") + PatchValueWriter.Number(level.Value);
}
