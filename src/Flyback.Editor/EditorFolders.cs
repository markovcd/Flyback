using Flyback.App.Canvas;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Midi;
using Flyback.App.Site;
using Flyback.App.Statistics;
using Flyback.App.Updates;
using Flyback.App.Windows;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

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

    /// <summary>Where the Graphics, Recording and Sound settings are read from and saved to.</summary>
    public string? OutputSettingsPath { get; init; }

    /// <summary>Where the update switch is read from and saved to.</summary>
    public string? UpdateSettingsPath { get; init; }

    /// <summary>Where the usage switch is read from and saved to.</summary>
    public string? UsageSettingsPath { get; init; }

    /// <summary>Where the Canvas section is read from and saved to.</summary>
    public string? CanvasSettingsPath { get; init; }

    /// <summary>Where the Files section is read from and saved to.</summary>
    public string? FileTypeSettingsPath { get; init; }

    /// <summary>Where the assistant's settings are read from and saved to, with its priority list beside them.</summary>
    public string? AssistantSettingsPath { get; init; }

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
        OutputSettingsPath = OutputSettings.File,
        UpdateSettingsPath = UpdateSettings.File,
        UsageSettingsPath = UsageSettings.File,
        CanvasSettingsPath = CanvasSettings.File,
        FileTypeSettingsPath = FileTypeSettings.File,
        AssistantSettingsPath = AssistantSettings.File,
        LayoutPath = WindowLayout.File,
        RecoveryFolder = Recovery.Folder,
        PluginFolder = PluginHost.DefaultDirectory,
        AllowedPluginsPath = PluginAllowances.DefaultFile,
        SharedPresetFolder = KeptSharedPresets.DefaultFolder,
    };
}
