using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Shell;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

/// <summary>
/// A window under test with settings of its own, and the controls of the settings
/// window the Picture, Recording, Sound, MIDI and Files tests find their way by.
/// </summary>
/// <remarks>
/// The controls are the state of the instrument rather than of a dialog, so they are
/// made once and lent to the window each time it opens — and a control may have one
/// parent. Opening it twice is the sequence that throws if the window ever stops
/// being taken apart first.
/// </remarks>
public abstract class OutputSettingsTest : EditorTest
{
    /// <summary>Where a window under test keeps its settings, so none land in the machine's own.</summary>
    private protected readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-output-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The real window, on the preset it opens with. Nothing is stubbed: with no
    /// plugins loaded the catalog is empty and the audio device is silent,
    /// which is the same path a machine with no sound backend takes.
    /// </summary>
    private protected MainWindow Open(string? settingsPath = null, PluginCatalog? plugins = null)
    {
        var window = NewMainWindow(new EditorSetup
        {
            Folders = new() { SettingsPath = settingsPath },
            Plugins = plugins ?? PluginCatalog.Empty,
        });

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private protected static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);

        var body = new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// The settings are on screen exactly when the size picker is in the tree,
    /// which is only ever inside the settings window.
    /// </summary>
    private protected static bool ShowingSettings(Visual within) =>
        All<ComboBox>(within).Any(c => c.ItemsSource is IEnumerable<string> items && items.Any(i => i.Contains(" x ")));

    private protected static ComboBox Size(Visual within) =>
        All<ComboBox>(within).Single(c => c.ItemsSource is IEnumerable<string> items && items.Any(i => i.Contains(" x ")));

    private protected static TabControl Tabs(Visual within) => All<TabControl>(within).Single(t => t.Name == "settingsTabs");

    private protected const string PictureTab = "Picture", RecordingTab = "Recording", SoundTab = "Sound", MidiTab = "MIDI", FilesTab = "Files";

    /// <summary>The name of the frame's cross, which answers as Cancel does.</summary>
    private protected const string Cross = "dismiss";

    private protected static NumericUpDown Quality(Visual within) => All<NumericUpDown>(within).Single(c => c.Name == "jpegQuality");

    private protected static ComboBox CountIn(Visual within) => All<ComboBox>(within).Single(c => c.Name == "countIn");

    private protected static CheckBox RewindFirst(Visual within) =>
        All<CheckBox>(within).Single(c => c.Name == "rewindBeforeTake");

    /// <summary>
    /// Named rather than found by content: it lives on the toolbar now and a
    /// glyph, unlike the label it replaced, has nothing a test can read.
    /// </summary>
    private protected static Button Record(MainWindow window) => Named<Button>(window, "record");

    private protected static ComboBox PreviewFrameRate(Visual within) =>
        All<ComboBox>(within).Single(c => c.Name == "previewFrameRate");
}
