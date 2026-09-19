using System.Globalization;

namespace Flyback.App.Updates;

/// <summary>
/// Where updates wait to be installed, beside the settings under the per-user data
/// folder.
/// </summary>
/// <remarks>
/// A version is ready exactly when a folder named for it exists: it is unpacked under
/// another name and renamed once it is complete, so a download cut off halfway leaves
/// nothing that looks installable — only a <c>.partial</c>, which the next download
/// clears. Beside each version, a count of the times installing it failed; and at the
/// top, the one sentence the last install left for the window to say.
/// </remarks>
internal sealed class UpdateFolder(string root)
{
    /// <summary>How many times one version is tried before it is left for the next release.</summary>
    public const int Attempts = 3;

    private const string PartialSuffix = ".partial";
    private const string FailuresName = "failures";
    private const string NoteName = "note.txt";
    private const string ReplacedName = "replaced.txt";

    public string Root { get; } = root;

    /// <summary>Where <paramref name="version"/> is unpacked once it is complete.</summary>
    public string VersionFolder(Version version) => Path.Combine(Root, version.ToString(3));

    /// <summary>Where <paramref name="version"/> is unpacked while it is not.</summary>
    public string PartialFolder(Version version) => VersionFolder(version) + PartialSuffix;

    /// <summary>Where <paramref name="packageName"/> is downloaded to.</summary>
    public string PartialPackage(string packageName) => Path.Combine(Root, packageName + PartialSuffix);

    /// <summary>
    /// The newest version waiting that is newer than <paramref name="running"/> and
    /// has not failed too often, or null.
    /// </summary>
    public Version? Pending(Version running) =>
        Ready()
            .Where(version => version > running && Failures(version) < Attempts)
            .OrderDescending()
            .FirstOrDefault();

    /// <summary>Every version unpacked and complete.</summary>
    public IEnumerable<Version> Ready() =>
        !Directory.Exists(Root)
            ? []
            : Directory.EnumerateDirectories(Root)
                .Select(folder => ReleaseFeed.Parse(Path.GetFileName(folder)))
                .OfType<Version>()
                .ToList();

    public int Failures(Version version)
    {
        try
        {
            return int.Parse(File.ReadAllText(Path.Combine(VersionFolder(version), FailuresName)), CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Counts one more failure to install <paramref name="version"/>, and says how many that makes.</summary>
    public int Failed(Version version)
    {
        var failures = Failures(version) + 1;

        File.WriteAllText(
            Path.Combine(VersionFolder(version), FailuresName),
            failures.ToString(CultureInfo.InvariantCulture));

        return failures;
    }

    /// <summary>
    /// Whether an install has left something to say that no window has said yet —
    /// which also means one was tried since the last start.
    /// </summary>
    public bool HasNote => File.Exists(Path.Combine(Root, NoteName));

    /// <summary>
    /// Leaves <paramref name="sentence"/> for the next window to say, and with it the
    /// release an install replaced, where it was one — which is where the changes
    /// the window shows start from.
    /// </summary>
    public void Note(string sentence, Version? replaced = null)
    {
        Directory.CreateDirectory(Root);

        var path = Path.Combine(Root, ReplacedName);

        if (replaced is null) File.Delete(path);
        else File.WriteAllText(path, replaced.ToString(3));

        File.WriteAllText(Path.Combine(Root, NoteName), sentence);
    }

    /// <summary>
    /// Takes away what <paramref name="running"/> has no more use for — versions no
    /// newer than itself, and anything half-downloaded — and hands back the note
    /// the last install left, once, with the release it replaced where that is known.
    /// Never throws: a folder still held by the process that just installed from it
    /// is cleared next time instead.
    /// </summary>
    public (string? Note, Version? Replaced) Tidy(Version? running)
    {
        var note = Take(NoteName);
        var replaced = Take(ReplacedName) is { } text ? ReleaseFeed.Parse(text) : null;

        ClearPartials();

        if (running is not null)
            foreach (var version in Ready().Where(version => version <= running))
                Delete(VersionFolder(version));

        return (string.IsNullOrEmpty(note) ? null : note, replaced);
    }

    /// <summary>What the file <paramref name="name"/> at the top says, taking it away; or null.</summary>
    private string? Take(string name)
    {
        try
        {
            var path = Path.Combine(Root, name);

            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path).Trim();
            File.Delete(path);

            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Takes away anything a download was cut off in the middle of.</summary>
    public void ClearPartials()
    {
        if (!Directory.Exists(Root)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(Root, "*" + PartialSuffix).ToList())
            Delete(entry);
    }

    /// <summary>Takes away every version waiting — for somebody who has switched updates off.</summary>
    public void Clear()
    {
        foreach (var version in Ready()) Delete(VersionFolder(version));
    }

    /// <summary>Takes away every version waiting other than <paramref name="kept"/>.</summary>
    public void ClearAllBut(Version kept)
    {
        foreach (var version in Ready().Where(version => version != kept)) Delete(VersionFolder(version));
    }

    private static void Delete(string entry)
    {
        try
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
