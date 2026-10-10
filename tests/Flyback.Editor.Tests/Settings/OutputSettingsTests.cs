using Flyback.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Tests.Settings;

/// <summary>
/// The Picture settings — size, preview rate, renderer — which live in the settings
/// window and are kept between launches (ADR-0082), and the toolbar that opens it.
/// </summary>
/// <remarks>
/// The controls are the state of the instrument rather than of a dialog, so they are
/// made once and lent to the window each time it opens — and a control may have one
/// parent. Opening it twice is the sequence that throws if the window ever stops
/// being taken apart first.
/// </remarks>
public partial class OutputSettingsTests : EditorTest
{
    /// <summary>Where a window under test keeps its settings, so none land in the machine's own.</summary>
    private readonly string settingsPath = Path.Combine(
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
    private MainWindow Open(string? settingsPath = null, PluginCatalog? plugins = null)
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

    private static void Select(MainWindow window, NodeInstance node)
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

    /// <summary>What every button in the window is labeled, in tree order.</summary>
    private static IEnumerable<string?> Buttons(MainWindow window) =>
        All<Button>(window).Select(b => b.Content as string);

    /// <summary>
    /// The settings are on screen exactly when the size picker is in the tree,
    /// which is only ever inside the settings window.
    /// </summary>
    private static bool ShowingSettings(Visual within) =>
        All<ComboBox>(within).Any(c => c.ItemsSource is IEnumerable<string> items && items.Any(i => i.Contains(" x ")));

    private static ComboBox Size(Visual within) =>
        All<ComboBox>(within).Single(c => c.ItemsSource is IEnumerable<string> items && items.Any(i => i.Contains(" x ")));

    private static TabControl Tabs(Visual within) => All<TabControl>(within).Single(t => t.Name == "settingsTabs");

    private const string PictureTab = "Picture", RecordingTab = "Recording", SoundTab = "Sound", MidiTab = "MIDI", FilesTab = "Files";

    /// <summary>The name of the frame's cross, which answers as Cancel does.</summary>
    private const string Cross = "dismiss";

    [AvaloniaFact]
    public void Nothing_selected_shows_no_settings()
    {
        var window = Open();

        ShowingSettings(window).ShouldBeFalse();
    }

    /// <summary>They moved to the settings window, and the Output's panel keeps only its knobs.</summary>
    [AvaloniaFact]
    public void Selecting_the_output_shows_no_settings()
    {
        var window = Open();

        Select(window, Editor(window).History.Patch.Output);

        ShowingSettings(window).ShouldBeFalse();
    }

    /// <summary>
    /// A tab a section, opening on the Picture tab, and one Save under them all —
    /// switching tabs is not saving, and Save keeps the tabs not showing as well.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_window_has_a_tab_for_each_section()
    {
        var window = Open();
        var dialog = OpenSettings(window, tab: PictureTab);
        var tabs = Tabs(dialog);

        tabs.Items.Cast<TabItem>().Select(t => (t.Header as TextBlock)?.Text)
            .ShouldBe(["Picture", "Sound", "MIDI", "Recording", "Canvas", "Files", "Assistant", "Decisions", "Privacy"]);
        tabs.SelectedIndex.ShouldBe(0);
        tabs.TabStripPlacement.ShouldBe(Dock.Left, "the sections are a list down the left");
        tabs.Items.Cast<TabItem>().Select(t => t.Bounds.X).Distinct().Count()
            .ShouldBe(1, "every tab fits in the one column");
        ShowingSettings(dialog).ShouldBeTrue("the Picture tab opens by default");

        var frame = All<Border>(dialog).Single(b => b.Name == "dialog");
        var size = frame.Bounds.Size;

        for (var tab = 0; tab < tabs.ItemCount; tab++)
        {
            tabs.SelectedIndex = tab;
            Settle(window);

            frame.Bounds.Size.ShouldBe(size, $"tab {tab}: the window keeps its size whichever section is showing");
        }

        All<Button>(dialog).Count(b => b.Content as string == "Save").ShouldBe(1, "one Save for every section");
    }

    /// <summary>
    /// The one this suite is really for. The controls are lent to a window built
    /// fresh each time, and have to be taken back from the last one first.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_window_opens_again_and_again()
    {
        var window = Open();

        for (var round = 0; round < 3; round++)
        {
            var dialog = OpenSettings(window);
            ShowingSettings(dialog).ShouldBeTrue($"round {round + 1}: the settings should be there");

            CloseSettings(window, dialog, round % 2 == 0 ? "save" : "cancel");
            ShowingSettings(window).ShouldBeFalse($"round {round + 1}: and gone with the window");
        }
    }

    [AvaloniaFact]
    public void Saving_keeps_the_settings_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Size(dialog).SelectedIndex = 1;
        CloseSettings(window, dialog);

        File.Exists(settingsPath).ShouldBeTrue();

        var next = Open(settingsPath);

        All<PreviewHost>(next).Single().Resolution.Width.ShouldBe(480);

        var again = OpenSettings(next);

        Size(again).SelectedIndex.ShouldBe(1);
    }
}
