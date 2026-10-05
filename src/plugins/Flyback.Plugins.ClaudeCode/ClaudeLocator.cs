namespace Flyback.Plugins.ClaudeCode;

/// <summary>Finds the <c>claude</c> program.</summary>
/// <remarks>
/// The path and the usual install folders, because a window started from a
/// launcher does not inherit the shell's path. Where it is can't be set: a
/// settings file that names a program to run is a settings file that runs code.
/// </remarks>
internal static class ClaudeLocator
{
    public static string FileName => OperatingSystem.IsWindows() ? "claude.exe" : "claude";

    /// <summary>The program, or null where none is installed.</summary>
    /// <param name="searched">Where to look; the path and the usual folders where null.</param>
    public static string? Find(IEnumerable<string>? searched = null)
    {
        foreach (var folder in searched ?? Folders())
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            try
            {
                var candidate = Path.Combine(folder, FileName);

                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A path entry with characters no path has.
            }
        }

        return null;
    }

    private static IEnumerable<string> Folders()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return folder.Trim('"');

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (home.Length > 0)
        {
            yield return Path.Combine(home, ".local", "bin");
            yield return Path.Combine(home, ".claude", "local");
        }

        yield return "/usr/local/bin";
        yield return "/opt/homebrew/bin";
    }
}
