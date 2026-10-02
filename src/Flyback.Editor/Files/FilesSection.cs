using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Gallery;
using Flyback.Editor.Inspect;
using Flyback.Editor.Settings;
using Flyback.Ui;

namespace Flyback.Editor.Files;

/// <summary>
/// The Files section of the settings window: which patch the window opens on, which
/// program opens Flyback's files (ADR-0127), and the library folder.
/// </summary>
/// <remarks>
/// The startup patch and the library are kept with the output settings; what opens
/// the files is kept in a section of its own.
/// </remarks>
internal sealed class FilesSection : ISettingsSection
{
    private readonly ComboBox opener = new Picker
    {
        Name = "fileOpener",
        ItemsSource = new[] { "Nothing", "The editor", "The viewer" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Which preset the window opens on at the next launch (ADR-0093). It reads
    /// <see cref="startupPatch"/>, and a click picks another from the gallery.
    /// </summary>
    private readonly Button defaultPreset = new()
    {
        Name = "defaultPreset",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The name on <see cref="defaultPreset"/>.</summary>
    private readonly TextBlock defaultPresetName = new()
    {
        Name = "defaultPresetName",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The name <see cref="defaultPreset"/> shows, and what Save writes.</summary>
    private string startupPatch = "";

    private readonly TextBox libraryBox = new()
    {
        Name = "library",
        PlaceholderText = "none",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>What the section was last saved as, and so what closing without Save puts it back to.</summary>
    private FileTypeSettings saved = new();

    /// <summary>Where <see cref="saved"/> is kept, or null to keep it nowhere.</summary>
    private readonly string? path;

    /// <summary>What tells the operating system, or null to tell it nothing.</summary>
    private readonly FileTypes? system;

    private readonly PresetSlot presets;
    private readonly IFilePickers pickers;
    private readonly OutputSettingRepository settings;
    private readonly PatchFiles files;

    private readonly Action<string, string?> report;

    /// <param name="presets">The presets the startup patch is named and picked from.</param>
    /// <param name="files">The patch's files, whose samples and pictures are looked for in the library.</param>
    public FilesSection(
        EditorFolders folders,
        EditorHost host,
        ReportLine report,
        PresetSlot presets,
        IFilePickers pickers,
        OutputSettingRepository settings,
        PatchFiles files)
    {
        path = folders.SettingsPath;
        system = host.FileTypes;
        this.presets = presets;
        this.pickers = pickers;
        this.settings = settings;
        this.files = files;
        this.report = (message, detail) => report.Say(message, detail);

        if (path is not null) saved = FileTypeSettings.Load(path);

        ToolTip.SetTip(opener,
            "What a double-click on a .fbk, .fbkb or .fbks file starts: nothing of Flyback's, "
            + "the editor with the patch open, or the viewer playing it. A .fbkp plugin always "
            + "reaches the editor, which asks before installing it.");

        View.Children.Add(BuildStartupPatch());
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

        View.Children.Add(BuildLibrary());

        View.Children.Add(new TextBlock
        {
            Text = "Where a sound or a picture a patch names is looked for when it is not beside the patch.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        opener.SelectedIndex = (int)saved.Opener;
    }

    public string Name => "Files";

    internal StackPanel View { get; } = new() { Spacing = 10, Width = 280 };

    Control ISettingsSection.View => View;

    public void Start()
    {
        Show();
        files.UseLibrary(settings.Current.Library, reread: false);
    }

    /// <summary>Puts what was last saved back on the controls.</summary>
    public void Show()
    {
        opener.SelectedIndex = (int)saved.Opener;

        ShowStartupPatch(settings.Current.DefaultPreset);
        libraryBox.Text = settings.Current.Library;
    }

    /// <summary>
    /// A program is registered again on every Save, so a copy that was moved points
    /// the files at where it is now. Nothing is taken back only once.
    /// </summary>
    public void Save()
    {
        settings.Change(into =>
        {
            into.DefaultPreset = startupPatch;
            into.Library = (libraryBox.Text ?? string.Empty).Trim();
        });

        files.UseLibrary(settings.Current.Library, reread: true);

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

    /// <summary>Which preset the window opens on, drawn as a picker that opens the gallery.</summary>
    private Control BuildStartupPatch()
    {
        ToolTip.SetTip(defaultPreset,
            "Which preset the window opens on the next time it starts. Picking one on the "
            + "toolbar right now does not change this — it only changes what is on the canvas.");

        // Drawn as the pickers are, so the row reads as a value to change rather
        // than a button to press, with the mark of a row that opens a window.
        var opens = Glyphs.Dots(12, Text.Muted);

        Grid.SetColumn(opens, 1);

        defaultPreset.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { defaultPresetName, opens },
        };

        defaultPreset.Padding = new Thickness(12, 5, 10, 7);
        defaultPreset.MinHeight = 32;
        defaultPreset.BorderThickness = new Thickness(1);
        defaultPreset.Bind(TemplatedControl.BackgroundProperty, defaultPreset.GetResourceObservable("ComboBoxBackground"));
        defaultPreset.Bind(TemplatedControl.BorderBrushProperty, defaultPreset.GetResourceObservable("ComboBoxBorderBrush"));

        defaultPreset.Click += async (_, _) =>
        {
            if (await presets.PickStartupPatchAsync(startupPatch) is { } chosen) ShowStartupPatch(chosen);
        };

        return InspectorRows.Field("Startup patch", defaultPreset);
    }

    /// <summary>A caption, the folder typed or picked, and a button that picks one.</summary>
    private Grid BuildLibrary()
    {
        ToolTip.SetTip(libraryBox,
            "A folder of sounds and pictures. A file chosen from inside it is named from it, "
            + "so the patch finds it on any machine with the same library.");

        var browse = new Button
        {
            Content = "…",
            Width = 32,
            FontSize = Text.Body,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        ToolTip.SetTip(browse, "Choose the library folder.");

        browse.Click += async (_, _) =>
        {
            var folders = await pickers.OpenFolder(new FolderPickerOpenOptions
            {
                Title = "Choose the library folder",
                AllowMultiple = false,
            });

            if (folders is [{ } picked] && picked.TryGetLocalPath() is { } path) libraryBox.Text = path;
        };

        var row = InspectorRows.Row("*,Auto", InspectorRows.SettingsGutter);
        var label = InspectorRows.Caption("Library", InspectorRows.SettingsGutter);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(libraryBox, 1);
        Grid.SetColumn(browse, 2);

        row.Children.Add(label);
        row.Children.Add(libraryBox);
        row.Children.Add(browse);

        return row;
    }

    /// <summary>
    /// Shows which preset the window starts on, naming the one chosen even where
    /// this launch does not offer it.
    /// </summary>
    /// <remarks>
    /// A plugin's preset, on a launch the plugin is away from. The window opened
    /// on the first patch instead, and naming that one here would have the next
    /// Save — of anything at all — write it over the choice. Nothing chosen yet
    /// names the patch the window opens on, which is what saving it then means.
    /// </remarks>
    private void ShowStartupPatch(string chosen)
    {
        var offered = presets.Ordered();

        startupPatch = chosen.Length > 0 ? chosen : offered[PresetLibrary.Opening(offered, chosen)].Name;
        defaultPresetName.Text = startupPatch;
    }
}
