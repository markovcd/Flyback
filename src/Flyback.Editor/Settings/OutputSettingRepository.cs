namespace Flyback.App.Settings;

/// <summary>The output settings shared by the startup audio setup and their editor section.</summary>
internal sealed class OutputSettingRepository
{
    public OutputSettingRepository(EditorSetup setup)
    {
        Current = setup.OutputSettingsPath is { } path ? OutputSettings.Load(path) : new OutputSettings();
    }

    public OutputSettings Current { get; set; }
}
