namespace Flyback.Plugins.Programs;

/// <summary>Finds a program by looking in folders.</summary>
/// <remarks>
/// The path and the usual install folders, because a window started from a
/// launcher does not inherit the shell's path. Where it is can't be set: a
/// settings file that names a program to run is a settings file that runs code.
/// </remarks>
internal static class ProgramLocator
{
    /// <summary>The program in the first folder that holds it, or null where none does.</summary>
    public static string? Find(string fileName, IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            try
            {
                var candidate = Path.Combine(folder, fileName);

                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A path entry with characters no path has.
            }
        }

        return null;
    }

    /// <summary>Each folder of the path.</summary>
    public static IEnumerable<string> PathFolders() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => folder.Trim('"'));

    /// <summary>The person's home folder, or empty where there is none.</summary>
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
