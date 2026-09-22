using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.App.Files;

namespace Flyback.App;

/// <summary>The Files section of the settings window: which program opens Flyback's files (ADR-0127).</summary>
internal sealed class FilesSection
{
    private readonly ComboBox opener = new Picker
    {
        Name = "fileOpener",
        ItemsSource = new[] { "Nothing", "The editor", "The viewer" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private FileTypeSettings saved = new();

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    /// <summary>What tells the operating system, or null to tell it nothing.</summary>
    private readonly FileTypes? system;

    private readonly Action<string, string?> report;

    internal FilesSection(string? path, FileTypes? system, Action<string, string?> report)
    {
        this.path = path;
        this.system = system;
        this.report = report;

        if (path is not null) saved = FileTypeSettings.Load(path);

        ToolTip.SetTip(opener,
            "What a double-click on a .fbk, .fbkb or .fbks file starts: nothing of Flyback's, "
            + "the editor with the patch open, or the viewer playing it. A .fbkp plugin always "
            + "reaches the editor, which asks before installing it.");

        View.Children.Add(InspectorRows.Field("Open with", opener));

        View.Children.Add(new TextBlock
        {
            Text = OperatingSystem.IsMacOS()
                ? "Finder always hands Flyback's files to Flyback, which passes them to the viewer "
                    + "when the viewer is chosen."
                : "Only for you, and only this copy of Flyback. Saving with Nothing takes back what "
                    + "Flyback registered. A program you picked yourself for these files stays picked.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        Show();
    }

    internal StackPanel View { get; } = new() { Spacing = 10, Width = 280 };

    internal FileOpener Opener => saved.Opener;

    /// <summary>Puts what was last saved back on the controls.</summary>
    internal void Show() => opener.SelectedIndex = (int)saved.Opener;

    /// <summary>
    /// A program is registered again on every Save, so a copy that was moved points
    /// the files at where it is now. Nothing is taken back only once.
    /// </summary>
    internal void Save()
    {
        var before = saved.Opener;

        saved = new FileTypeSettings { Opener = (FileOpener)Math.Max(0, opener.SelectedIndex) };

        try
        {
            if (saved.Opener != FileOpener.None || before != FileOpener.None) system?.Apply(saved.Opener);
        }
        catch (Exception ex)
        {
            report($"Could not change what opens Flyback's files: {ex.Message}", null);
        }

        if (path is null) return;

        try
        {
            saved.Save(path);
        }
        catch (Exception ex)
        {
            report($"Could not save the file settings: {ex.Message}", path);
        }
    }
}
