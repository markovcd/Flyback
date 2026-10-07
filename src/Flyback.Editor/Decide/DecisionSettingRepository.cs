using Flyback.Plugins.Decide;

namespace Flyback.Editor.Decide;

/// <summary>The decision settings this window reads and saves, kept in the settings file's own section.</summary>
internal sealed class DecisionSettingRepository(EditorFolders folders)
{
    public DecisionSettings Current { get; } = folders.SettingsPath is null ? new DecisionSettings() : DecisionSettings.Load(folders.SettingsPath);

    /// <summary>Where they are written, or null where they are kept in memory.</summary>
    public string? Path => folders.SettingsPath;

    public void Save()
    {
        if (folders.SettingsPath is not null) Current.Save(folders.SettingsPath);
    }
}
