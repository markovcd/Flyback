using Flyback.Plugins.Programs;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>Finds the <c>claude</c> program.</summary>
internal static class ClaudeLocator
{
    public static string FileName => OperatingSystem.IsWindows() ? "claude.exe" : "claude";

    /// <summary>The program, or null where none is installed.</summary>
    /// <param name="searched">Where to look; the path and the usual folders where null.</param>
    public static string? Find(IEnumerable<string>? searched = null) =>
        ProgramLocator.Find(FileName, searched ?? Folders());

    private static IEnumerable<string> Folders()
    {
        foreach (var folder in ProgramLocator.PathFolders()) yield return folder;

        if (ProgramLocator.Home.Length > 0)
        {
            yield return Path.Combine(ProgramLocator.Home, ".local", "bin");
            yield return Path.Combine(ProgramLocator.Home, ".claude", "local");
        }

        yield return "/usr/local/bin";
        yield return "/opt/homebrew/bin";
    }
}
