using Flyback.Core;
using Flyback.Editor.Canvas;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Midi;
using Flyback.Editor.Shell;
using Flyback.Editor.Site;
using Flyback.Editor.Statistics;
using Flyback.Editor.Updates;
using Flyback.Editor.Windows;
using Flyback.Host;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Ui;

namespace Flyback.Editor.Composition;

/// <summary>What the editor is opened into on this machine: the desktop program's own start.</summary>
internal static class ThisMachine
{
    /// <summary>Where this machine keeps everything, and what it reaches.</summary>
    public static EditorSetup Setup(Usage usage) => new()
    {
        Folders = Folders(),
        Host = Host(),
        Usage = usage,
    };

    /// <summary>Where this machine keeps everything.</summary>
    public static EditorFolders Folders() => new()
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
        ModelFolder = ModelStore.DefaultRoot,
    };

    /// <summary>A desktop window on this machine, reaching the preset site this copy was built for.</summary>
    public static EditorHost Host() => new()
    {
        PresetSite = PresetSite.Built,
        FileTypes = FileTypes.ForThisCopy(),
        Relaunch = Restart.Launch,
    };
}
