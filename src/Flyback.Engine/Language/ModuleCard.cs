namespace Flyback.Engine.Language;

/// <summary>
/// What the <c>///</c> lines above a pack's <c>module</c> say of it: its help, and the
/// category, words and glyph a plugin's module would declare in C#.
/// </summary>
/// <param name="Help">Every line that is not one of the named fields, joined with spaces.</param>
/// <param name="Category">The section it is listed under, or null where the lines name none.</param>
/// <param name="Words">What else a person might call it, apart by commas.</param>
/// <param name="Glyph">The SVG path drawn on its card, or null.</param>
public sealed record ModuleCard(string Help, string? Category, string Words, string? Glyph)
{
    /// <summary>
    /// The card written in the unbroken run of <c>///</c> lines straight above
    /// <paramref name="line"/>, counted from one.
    /// </summary>
    /// <remarks>
    /// A line may hold several fields apart by <c>·</c>, as in
    /// <c>/// category: Patterns · words: 3d, tunnel</c>.
    /// </remarks>
    public static ModuleCard Above(IReadOnlyList<string> lines, int line)
    {
        var first = line - 1;

        while (first > 0 && lines[first - 1].TrimStart().StartsWith("///", StringComparison.Ordinal)) first--;

        var help = new List<string>();
        string? category = null, words = null, glyph = null;

        foreach (var written in lines.Skip(first).Take(line - 1 - first))
        {
            var text = written.TrimStart()[3..].Trim();
            var parts = text.Split('·', StringSplitOptions.TrimEntries);

            if (!parts.All(Field))
            {
                if (text.Length > 0) help.Add(text);
                continue;
            }

            foreach (var part in parts)
            {
                var colon = part.IndexOf(':');
                var value = part[(colon + 1)..].Trim();

                switch (part[..colon].Trim())
                {
                    case "category": category = value; break;
                    case "words": words = value; break;
                    case "glyph": glyph = value; break;
                }
            }
        }

        return new ModuleCard(string.Join(' ', help), category, words ?? "", glyph);
    }

    /// <summary>Whether a part of a line is one of the named fields, so a sentence with a colon in it stays help.</summary>
    private static bool Field(string part) =>
        part.IndexOf(':') is > 0 and var colon && part[..colon].Trim() is "category" or "words" or "glyph";
}
