using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

internal sealed class AssistantSettingRepository
{
    private readonly EditorFolders folders;

    public AssistantSettingRepository(EditorFolders folders, AssistantSettings? saved = null)
    {
        this.folders = folders;
        Current = saved ?? Load();
    }

    public AssistantSettings Current { get; }

    private AssistantSettings Load() =>
        folders.AssistantSettingsPath is null
            ? new AssistantSettings()
            : AssistantSettings.Load(folders.AssistantSettingsPath);

    public void Save()
    {
        if (folders.AssistantSettingsPath is not null) Current.Save(folders.AssistantSettingsPath);
    }

    /// <summary>Where the priority list is read from: beside the settings, or nowhere where they are kept in memory.</summary>
    public string? PriorityFile => folders.AssistantSettingsPath is { } path
        ? AssistantSettings.PriorityFileBeside(path)
        : null;

    /// <summary>
    /// The budget and the priority list as they stand. The list is read from its
    /// file every time, so an edit to it counts from the next conversation, and
    /// from the next save as far as the canvas is concerned.
    /// </summary>
    public ProsePolicy GetProsePolicy() => Current.Prose(folders.AssistantSettingsPath);
}
