namespace Flyback.App.Settings;

/// <summary>The output settings shared by the startup audio setup and their editor section.</summary>
internal sealed class OutputSettingRepository
{
    public OutputSettingRepository(EditorFolders folders, EditorHost host)
    {
        // A page's picture never leaves its box, so it is drawn small.
        Current = folders.OutputSettingsPath is { } path ? OutputSettings.Load(path)
            : host.InPage ? new OutputSettings { Width = 480, Height = 270 }
            : new OutputSettings();
    }

    public OutputSettings Current { get; set; }
}
