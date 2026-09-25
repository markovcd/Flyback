using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Statistics;
using Flyback.App.Updates;
using Flyback.Core.Compile;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// What the editor's window is opened into: where this machine keeps what it
/// saves, what it reaches outside itself, and what this launch was asked to do.
/// </summary>
/// <remarks>
/// Everything left unset keeps nothing, reads nothing and reaches no network, which
/// is what a test gets by default: none of them has any business with the folders
/// or the settings of the machine running it. The program itself starts from
/// <see cref="ThisMachine"/>.
/// </remarks>
public sealed record EditorSetup : IIlCompilerSetup
{
    /// <summary>
    /// Where the kept groups live. Null is the usual place; a path is for the
    /// tests, which must not write into the folder a person's own groups are in.
    /// </summary>
    public string? GroupFolder { get; init; }

    /// <summary>Where the presets somebody saved live. Null keeps none and offers no way to save one.</summary>
    public string? PresetFolder { get; init; }

    /// <summary>Where the gallery's thumbnails are kept between runs. Null draws them afresh each run.</summary>
    public string? ThumbnailFolder { get; init; }

    /// <summary>Where the Graphics, Recording and Sound settings are read from and saved to.</summary>
    public string? OutputSettingsPath { get; init; }

    /// <summary>Where the Updates section is read from and saved to.</summary>
    public string? UpdateSettingsPath { get; init; }

    /// <summary>Where the Usage section is read from and saved to.</summary>
    public string? UsageSettingsPath { get; init; }

    /// <summary>Where the Canvas section is read from and saved to.</summary>
    public string? CanvasSettingsPath { get; init; }

    /// <summary>Where the Files section is read from and saved to.</summary>
    public string? FileTypeSettingsPath { get; init; }

    /// <summary>Where the assistant's settings are read from and saved to, with its priority list beside them.</summary>
    public string? AssistantSettingsPath { get; init; }

    /// <summary>Where the window's size, place and panels are kept (ADR-0121).</summary>
    public string? LayoutPath { get; init; }

    /// <summary>
    /// Where unsaved work is kept against a crash, and where what a crash left is
    /// looked for (ADR-0103).
    /// </summary>
    public string? RecoveryFolder { get; init; }

    /// <summary>Where a plugin package opened in the window is installed.</summary>
    public string? PluginFolder { get; init; }

    /// <summary>What the Files section tells the operating system.</summary>
    public FileTypes? FileTypes { get; init; }

    /// <summary>What this run says about itself (ADR-0094).</summary>
    public Usage Usage { get; init; } = Usage.Off;

    /// <summary>The plugins loaded before any window existed, already installed in the module catalog.</summary>
    public PluginCatalog Plugins { get; init; } = PluginCatalog.Empty;

    /// <summary>
    /// Starts Flyback again once this window has closed, which is what loads a plugin
    /// just installed. Null offers no restart.
    /// </summary>
    public Action<Reopen?>? Relaunch { get; init; }

    /// <summary>The site the gallery lists shared presets from and the plugins window shared plugins.</summary>
    public Uri? PresetSite { get; init; }

    /// <summary>
    /// A file to open once there is a window for it, or null for the usual start on
    /// the default preset — see <see cref="Startup.OpenPath"/>.
    /// </summary>
    public string? OpenPath { get; init; }

    /// <summary>The shared preset a restart was carrying, by its id on the preset site.</summary>
    public string? OpenShared { get; init; }

    /// <summary>
    /// Keep the CPU's programs on the interpreter for the whole run — see
    /// <see cref="Startup.Interpreted"/>.
    /// </summary>
    public bool Interpreted { get; init; }

    /// <summary>What the last update and any plugin just installed did, said once on the status bar.</summary>
    public string? OpeningNote { get; init; }

    /// <summary>
    /// What the release just installed changed, shown once in a dialog when the
    /// window opens in place of <see cref="OpeningNote"/>.
    /// </summary>
    public ReleaseNotes? WhatsNew { get; init; }

    /// <summary>Where this machine keeps everything, and what it reaches: the program's own start.</summary>
    public static EditorSetup ThisMachine(Usage usage) => new()
    {
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
        FileTypes = FileTypes.ForThisCopy(),
        Usage = usage,
        Relaunch = Restart.Launch,
        PresetSite = global::Flyback.App.PresetSite.Local,
    };
}
