using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

internal sealed class AssistantSettingRepository
{
    private readonly IAssistantSetup assistantSetup;

    public AssistantSettingRepository(IAssistantSetup assistantSetup, AssistantSettings? saved = null)
    {
        this.assistantSetup = assistantSetup;
        Current = saved ?? Load();
    }
    
    public AssistantSettings Current { get; }

    private AssistantSettings Load() =>
        assistantSetup.AssistantSettingsPath is null
            ? new AssistantSettings()
            : AssistantSettings.Load(assistantSetup.AssistantSettingsPath);

    public void Save()
    {
        if (assistantSetup.AssistantSettingsPath is not null) Current.Save(assistantSetup.AssistantSettingsPath);
    }
    
    /// <summary>Where the priority list is read from: beside the settings, or nowhere where they are kept in memory.</summary>
    public string? PriorityFile => assistantSetup.AssistantSettingsPath is null
        ? null
        : Path.Combine(
            Path.GetDirectoryName(assistantSetup.AssistantSettingsPath) ?? string.Empty, 
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