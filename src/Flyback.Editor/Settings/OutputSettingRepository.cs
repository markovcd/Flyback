using Flyback.Ui;

namespace Flyback.Editor.Settings;

/// <summary>
/// The output settings in force, shared by the settings sections that show them, the
/// knob panel and startup, and kept in the settings file's output section.
/// </summary>
internal sealed class OutputSettingRepository
{
    private readonly string? path;
    private readonly ReportLine report;

    public OutputSettingRepository(EditorFolders folders, EditorHost host, ReportLine report)
    {
        path = folders.SettingsPath;
        this.report = report;

        // A page's picture never leaves its box, so it is drawn small.
        Current = path is not null ? OutputSettings.Load(path)
            : host.InPage ? new OutputSettings { Width = 480, Height = 270 }
            : new OutputSettings();
    }

    /// <summary>The settings in force, changed in place; set from outside only by a run that keeps nothing, such as a shot.</summary>
    public OutputSettings Current { get; init; }

    /// <summary>Makes <paramref name="change"/> to the settings in force and keeps it, handing back what they were before.</summary>
    public OutputSettings Change(Action<OutputSettings> change)
    {
        var before = Current.Copy();

        change(Current);
        Save();

        return before;
    }

    /// <summary>Writes the settings in force, saying so if it cannot.</summary>
    public void Save()
    {
        if (path is null) return;

        try
        {
            Current.Save(path);
        }
        catch (Exception ex)
        {
            report.Say($"Could not save the output settings: {ex.Message}", path);
        }
    }
}
