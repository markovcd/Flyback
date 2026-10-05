using Flyback.Plugins.Programs;

namespace Flyback.Plugins.Codex;

/// <summary>Finds the <c>codex</c> program.</summary>
internal static class CodexLocator
{
    public static string FileName => OperatingSystem.IsWindows() ? "codex.exe" : "codex";

    /// <summary>The program, or null where none is installed.</summary>
    /// <param name="searched">Where to look; the path and the usual folders where null.</param>
    public static string? Find(IEnumerable<string>? searched = null) =>
        ProgramLocator.Find(FileName, searched ?? Folders());

    private static IEnumerable<string> Folders()
    {
        foreach (var folder in ProgramLocator.PathFolders()) yield return folder;

        if (ProgramLocator.Home.Length > 0) yield return Path.Combine(ProgramLocator.Home, ".local", "bin");

        foreach (var folder in AppFolders()) yield return folder;

        yield return "/usr/local/bin";
        yield return "/opt/homebrew/bin";
        yield return "/Applications/Codex.app/Contents/Resources";
    }

    /// <summary>The desktop app keeps its program in a folder named for the build, newest first.</summary>
    private static IEnumerable<string> AppFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (local.Length == 0) return [];

        var bin = Path.Combine(local, "OpenAI", "Codex", "bin");

        try
        {
            return Directory.Exists(bin)
                ? new DirectoryInfo(bin).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Select(d => d.FullName).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
