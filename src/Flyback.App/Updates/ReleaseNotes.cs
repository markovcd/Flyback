namespace Flyback.App.Updates;

/// <summary>
/// What changed in one release, as its section of the changelog says it — shown once
/// in the window that opens after that release installed itself.
/// </summary>
/// <remarks>
/// Read from the changelog the build carries inside itself, so a version can only
/// describe itself and never needs the network to do it. A release's section is
/// headed with its number (<c>## 0.4.0 — 2026-09-30</c>); a build whose changelog
/// still calls it Unreleased has no notes, and says it was updated on the status bar
/// instead.
/// </remarks>
/// <param name="Version">The release described.</param>
/// <param name="Text">The section's lines under its heading, in the changelog's Markdown.</param>
public sealed record ReleaseNotes(Version Version, string Text)
{
    private const string Resource = "CHANGELOG.md";

    /// <summary>The notes for <paramref name="version"/> in the changelog built in, or null.</summary>
    public static ReleaseNotes? Of(Version version)
    {
        try
        {
            using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(Resource);

            if (stream is null) return null;

            using var reader = new StreamReader(stream);

            return Of(version, reader.ReadToEnd());
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static ReleaseNotes? Of(Version version, string changelog)
    {
        var heading = "## " + version.ToString(3);
        var lines = changelog.ReplaceLineEndings("\n").Split('\n');

        var start = Array.FindIndex(lines, line => line == heading || line.StartsWith(heading + " ", StringComparison.Ordinal));

        if (start < 0) return null;

        var section = lines
            .Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));

        var text = string.Join('\n', section).Trim();

        return text.Length == 0 ? null : new ReleaseNotes(version, text);
    }
}
