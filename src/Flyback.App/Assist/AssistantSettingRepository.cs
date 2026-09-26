using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

internal sealed class AssistantSettingRepository
{
    private readonly EditorSetup editorSetup;

    public AssistantSettingRepository(EditorSetup editorSetup, AssistantSettings? saved = null)
    {
        this.editorSetup = editorSetup;
        Current = saved ?? Load();
    }
    
    public AssistantSettings Current { get; }

    private AssistantSettings Load() =>
        editorSetup.AssistantSettingsPath is null
            ? new AssistantSettings()
            : AssistantSettings.Load(editorSetup.AssistantSettingsPath);

    public void Save()
    {
        if (editorSetup.AssistantSettingsPath is not null) Current.Save(editorSetup.AssistantSettingsPath);
    }
    
    /// <summary>Where the priority list is read from: beside the settings, or nowhere where they are kept in memory.</summary>
    public string? PriorityFile => editorSetup.AssistantSettingsPath is null
        ? null
        : Path.Combine(
            Path.GetDirectoryName(editorSetup.AssistantSettingsPath) ?? string.Empty,
            Path.GetFileName(Flyback.Plugins.Assist.PriorityModules.File));

    private IReadOnlySet<string> PriorityModules => PriorityFile is { } file
        ? Flyback.Plugins.Assist.PriorityModules.Load(file)
        : Flyback.Plugins.Assist.PriorityModules.Parse(Flyback.Plugins.Assist.PriorityModules.Shipped);
    
    /// <summary>
    /// The budget and the priority list as they stand. The list is read from its
    /// file every time, so an edit to it counts from the next conversation, and
    /// from the next save as far as the canvas is concerned.
    /// </summary>
    public ProsePolicy GetProsePolicy() => new(Current.ProseBudget, PriorityModules);
}