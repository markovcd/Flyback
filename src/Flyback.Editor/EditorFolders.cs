using Flyback.Core;
using Flyback.Editor.Canvas;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Site;
using Flyback.Editor.Windows;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Midi;
using Flyback.Ui;

namespace Flyback.Editor;

/// <summary>Where this machine keeps what the editor reads and saves.</summary>
/// <remarks>Each one left null keeps nothing and reads nothing, which is what a test gets by default.</remarks>
public sealed record EditorFolders : IPresetFolder
{
    /// <summary>Where the kept groups live. Null keeps them nowhere that outlasts the test.</summary>
    public string? GroupFolder { get; init; }

    /// <summary>Where the instrument profiles of the user's own are read from. Null knows only the shipped ones.</summary>
    public string? InstrumentFolder { get; init; }

    /// <summary>Where the presets somebody saved live. Null keeps none and offers no way to save one.</summary>
    public string? PresetFolder { get; init; }

    /// <summary>Where the gallery's thumbnails are kept between runs. Null draws them afresh each run.</summary>
    public string? ThumbnailFolder { get; init; }

    /// <summary>
    /// Where every setting is read from and saved to, a section a concern
    /// (<see cref="SettingsFile"/>), with the assistant's priority list beside it.
    /// </summary>
    public string? SettingsPath { get; init; }

    /// <summary>Where conversation logs are written when logging is on. Null writes them to the user's data folder.</summary>
    public string? ConversationLogFolder { get; init; }

    /// <summary>Where the window's size, place and panels are kept (ADR-0121).</summary>
    public string? LayoutPath { get; init; }

    /// <summary>
    /// Where unsaved work is kept against a crash, and where what a crash left is
    /// looked for (ADR-0103).
    /// </summary>
    public string? RecoveryFolder { get; init; }

    /// <summary>Where a plugin package opened in the window is installed.</summary>
    public string? PluginFolder { get; init; }

    /// <summary>Where installing a plugin records the yes that lets it load, or null to record nothing.</summary>
    public string? AllowedPluginsPath { get; init; }

    /// <summary>Where each shared preset opened is kept, to open again while the site does not answer. Null keeps none.</summary>
    public string? SharedPresetFolder { get; init; }

    /// <summary>Where this machine keeps everything.</summary>
    public static EditorFolders ThisMachine() => new()
    {
        GroupFolder = GroupLibrary.DefaultFolder,
        InstrumentFolder = InstrumentLibrary.UserFolder,
        PresetFolder = PresetLibrary.DefaultFolder,
        ThumbnailFolder = ThumbnailStore.DefaultFolder,
        SettingsPath = SettingsFile.Path,
        LayoutPath = WindowLayout.File,
        RecoveryFolder = Recovery.Folder,
        PluginFolder = PluginHost.DefaultDirectory,
        AllowedPluginsPath = PluginAllowances.DefaultFile,
        SharedPresetFolder = KeptSharedPresets.DefaultFolder,
    };
}
