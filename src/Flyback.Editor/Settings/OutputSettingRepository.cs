namespace Flyback.App.Settings;

/// <summary>The output settings shared by the startup audio setup and their editor section.</summary>
internal sealed class OutputSettingRepository
{
    public OutputSettingRepository(EditorSetup setup)
    {
        // A page's picture never leaves its box, so it is drawn small.
        Current = setup.OutputSettingsPath is { } path ? OutputSettings.Load(path)
            : setup.InPage ? new OutputSettings { Width = 480, Height = 270 }
            : new OutputSettings();
    }

    public OutputSettings Current { get; set; }
}
