using Flyback.Editor.Statistics;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor;

/// <summary>
/// What the editor's window is opened into, handed to its container in pieces: each
/// part takes only the piece it reads.
/// </summary>
/// <remarks>
/// Everything left unset keeps nothing, reads nothing and reaches no network, which
/// is what a test gets by default. The program itself starts from <see cref="ThisMachine"/>.
/// </remarks>
public sealed record EditorSetup
{
    /// <summary>Where this machine keeps what the editor reads and saves.</summary>
    public EditorFolders Folders { get; init; } = new();

    /// <summary>What this launch was asked to do.</summary>
    public EditorLaunch Launch { get; init; } = new();

    /// <summary>What the editor is running in, and what it reaches.</summary>
    public EditorHost Host { get; init; } = new();

    /// <summary>What this run says about itself (ADR-0094).</summary>
    public Usage Usage { get; init; } = Usage.Off;

    /// <summary>The plugins loaded before any window existed, already installed in the module catalog.</summary>
    internal PluginCatalog Plugins { get; init; } = PluginCatalog.Empty;

    /// <summary>Where this machine keeps everything, and what it reaches: the program's own start.</summary>
    public static EditorSetup ThisMachine(Usage usage) => new()
    {
        Folders = EditorFolders.ThisMachine(),
        Host = EditorHost.ThisMachine(),
        Usage = usage,
    };
}
