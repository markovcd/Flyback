namespace Flyback.App.Updates;

/// <summary>
/// What changed between the release an update replaced and the one it installed, as
/// the changelog says it — shown once in the window that opens after.
/// </summary>
/// <remarks>
/// Read from the changelog the build carries inside itself, so a version can only
/// describe itself and those before it, and never needs the network to do it. A
/// release's section is headed with its number (<c>## 0.4.0 — 2026-09-30</c>); a
/// build whose changelog still calls it Unreleased has no notes, and says it was
/// updated on the status bar instead.
/// </remarks>
/// <param name="Version">The release installed.</param>
/// <param name="Since">The release it replaced, or null where that is not known.</param>
/// <param name="Sections">One a release, newest first as the changelog has them.</param>
public sealed record ReleaseNotes(Version Version, Version? Since, IReadOnlyList<ReleaseNotes.Section> Sections)
{
    private const string Resource = "CHANGELOG.md";

    /// <summary>One release's part of the changelog.</summary>
    /// <param name="Heading">What its heading says after the <c>##</c>: the number, and the date.</param>
    /// <param name="Text">The lines under the heading, in the changelog's Markdown.</param>
    public sealed record Section(string Heading, string Text);

    /// <summary>
    /// The notes for every release after <paramref name="since"/> up to
    /// <paramref name="version"/> in the changelog built in — only
    /// <paramref name="version"/>'s own where <paramref name="since"/> is null — or
    /// null where the changelog has no section for <paramref name="version"/>.
    /// </summary>
    public static ReleaseNotes? Of(Version version, Version? since = null)
    {
        try
        {
            using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(Resource);

            if (stream is null) return null;

            using var reader = new StreamReader(stream);

            return Of(version, since, reader.ReadToEnd());
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static ReleaseNotes? Of(Version version, Version? since, string changelog)
    {
        // A replaced copy no older than this one is a reinstall, or a downgrade by
        // hand, and has nothing in between to tell.
        if (since is not null && since >= version) since = null;

        var sections = Read(changelog)
            .Where(section => section.Version == version
                || (since is not null && section.Version > since && section.Version < version))
            .ToList();

        if (!sections.Any(section => section.Version == version)) return null;

        return new ReleaseNotes(version, since, [.. sections.Select(section => section.Section)]);
    }

    /// <summary>Every section headed with a release's number, and that number.</summary>
    private static IEnumerable<(Version Version, Section Section)> Read(string changelog)
    {
        string? heading = null;
        Version? number = null;
        var lines = new List<string>();

        foreach (var line in changelog.ReplaceLineEndings("\n").Split('\n').Append("## "))
        {
            if (!line.StartsWith("## ", StringComparison.Ordinal))
            {
                lines.Add(line);
                continue;
            }

            var text = string.Join('\n', lines).Trim();

            if (heading is not null && number is not null && text.Length > 0)
                yield return (number, new Section(heading, text));

            heading = line[3..].Trim();
            number = ReleaseFeed.Parse(heading.Split(' ')[0]);
            lines.Clear();
        }
    }
}
