namespace Flyback.App.Updates;

/// <summary>
/// Puts a new version's files over an installed copy, and puts the old ones back if
/// that cannot be finished.
/// </summary>
/// <remarks>
/// What a release ships is replaced and nothing else is touched, because an
/// installed folder holds things a release does not know about — a plugin somebody
/// dropped into <c>plugins/</c>, above all. The one exception is a plugin folder the
/// release ships, which is replaced whole: a plugin is loaded from every assembly in
/// its folder, and one its new version stopped shipping must not be left behind to
/// be loaded beside it.
/// <para>
/// Every file replaced is moved into <see cref="BackupName"/> inside the copy first —
/// a rename on the same volume, so it is cheap and cannot half-happen — and the
/// folder is only deleted once everything is in. One still there at the start is an
/// install that was cut off, and it is put back before anything else happens.
/// </para>
/// </remarks>
internal static class Installer
{
    public const string BackupName = ".update-backup";

    /// <param name="payload">The new version, unpacked.</param>
    /// <param name="root">The copy to update.</param>
    /// <param name="copying">Called before each file is put in, for a test to make one fail.</param>
    public static void Install(string payload, string root, Action<string>? copying = null)
    {
        var backup = Path.Combine(root, BackupName);

        if (Directory.Exists(backup)) Restore(backup, root);

        var files = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(payload, file))
            .ToList();

        var plugins = files.Select(PluginFolder).OfType<string>().Distinct().ToList();
        var copied = new List<string>();

        try
        {
            foreach (var plugin in plugins)
            {
                var installed = Path.Combine(root, plugin);

                if (!Directory.Exists(installed)) continue;

                var kept = Path.Combine(backup, plugin);
                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                Retry(() => Directory.Move(installed, kept));
            }

            foreach (var file in files)
            {
                var target = Path.Combine(root, file);

                if (File.Exists(target))
                {
                    var kept = Path.Combine(backup, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                    Retry(() => File.Move(target, kept));
                }

                copying?.Invoke(file);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Retry(() => File.Copy(Path.Combine(payload, file), target, overwrite: true));
                copied.Add(target);
            }
        }
        catch
        {
            foreach (var target in copied) File.Delete(target);

            Restore(backup, root);
            throw;
        }

        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
    }

    /// <summary>
    /// The plugin folder a shipped file is in — <c>plugins/WinIO</c>, or inside a
    /// bundle <c>Contents/MacOS/plugins/MacIO</c> — or null for a file in none.
    /// </summary>
    internal static string? PluginFolder(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var at = Array.IndexOf(parts, "plugins");

        return at >= 0 && at + 2 < parts.Length ? Path.Combine(parts[..(at + 2)]) : null;
    }

    /// <summary>Moves every file in <paramref name="backup"/> back to where it came from.</summary>
    private static void Restore(string backup, string root)
    {
        // Nothing had been moved aside yet.
        if (!Directory.Exists(backup)) return;

        foreach (var kept in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).ToList())
        {
            var target = Path.Combine(root, Path.GetRelativePath(backup, kept));

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Retry(() => File.Move(kept, target, overwrite: true));
        }

        Directory.Delete(backup, recursive: true);
    }

    /// <summary>
    /// A few tries, for a file held for a moment by something that is not ours — a
    /// virus scanner looking at what was just written, typically, or the old
    /// process's last handles closing.
    /// </summary>
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
