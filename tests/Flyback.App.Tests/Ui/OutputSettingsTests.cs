using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Graphics settings — size, preview rate, renderer — which live in the settings
/// window and are kept between launches (ADR-0082), and the toolbar that opens it.
/// </summary>
/// <remarks>
/// The controls are the state of the instrument rather than of a dialog, so they are
/// made once and lent to the window each time it opens — and a control may have one
/// parent. Opening it twice is the sequence that throws if the window ever stops
/// being taken apart first.
/// </remarks>
public class OutputSettingsTests : UiTest
{
    /// <summary>Where a window under test keeps its settings, so none land in the machine's own.</summary>
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-output-settings-" + Guid.NewGuid().ToString("N"),
        "output.json");

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
    private MainWindow Open(string? settingsPath = null)
    {
        var window = NewMainWindow(new EditorSetup { OutputSettingsPath = settingsPath });

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

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

    /// <summary>
    /// A toolbar control by the name it was given. The buttons up there are
    /// glyphs now, and a glyph is a poor thing to write an assertion against.
    /// </summary>
    private static T Named<T>(Visual within, string name)
        where T : Control =>
        All<T>(within).Single(c => c.Name == name);

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

    /// <summary>
    /// Presses the settings button, waits for the window it puts up, and turns to
    /// <paramref name="tab"/> — the Graphics tab unless told otherwise.
    /// </summary>
    private static ModalOverlay OpenSettings(MainWindow window, int tab = GraphicsTab)
    {
        Named<Button>(window, "settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        var dialog = All<ModalOverlay>(window).Single();

        if (tab != 0)
        {
            Tabs(dialog).SelectedIndex = tab;
            Settle(window);
        }

        return dialog;
    }

    private static TabControl Tabs(Visual within) => All<TabControl>(within).Single(t => t.Name == "settingsTabs");

    private const int GraphicsTab = 0, RecordingTab = 2, SoundTab = 3, MidiTab = 4;

    /// <summary>Answers the settings window by its Save, or by its cross.</summary>
    private static void CloseSettings(MainWindow window, ModalOverlay dialog, bool save) =>
        CloseSettings(window, dialog, save ? "Save" : Cross);

    /// <summary>What <see cref="CloseSettings(MainWindow, ModalOverlay, string)"/> takes to mean the frame's cross.</summary>
    private const string Cross = "dismiss";

    /// <summary>Answers the settings window by the button labeled <paramref name="by"/>, or by its cross.</summary>
    private static void CloseSettings(MainWindow window, ModalOverlay dialog, string by)
    {
        All<Button>(dialog)
            .Single(b => by == Cross ? b.Name == Cross : b.Content as string == by)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

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
    /// A tab a section, opening on the Graphics tab, and one Save under them all —
    /// switching tabs is not saving, and Save keeps the tabs not showing as well.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_window_has_a_tab_for_each_section()
    {
        var window = Open();
        var dialog = OpenSettings(window, tab: 0);
        var tabs = Tabs(dialog);

        tabs.Items.Cast<TabItem>().Select(t => (t.Header as TextBlock)?.Text)
            .ShouldBe(["Graphics", "Canvas", "Recording", "Sound", "MIDI", "Assistant", "Files", "Updates", "Usage"]);
        tabs.SelectedIndex.ShouldBe(0);
        tabs.TabStripPlacement.ShouldBe(Dock.Left, "the sections are a list down the left");
        tabs.Items.Cast<TabItem>().Select(t => t.Bounds.X).Distinct().Count()
            .ShouldBe(1, "every tab fits in the one column");
        ShowingSettings(dialog).ShouldBeTrue("the Graphics tab opens by default");

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

            CloseSettings(window, dialog, save: round % 2 == 0);
            ShowingSettings(window).ShouldBeFalse($"round {round + 1}: and gone with the window");
        }
    }

    [AvaloniaFact]
    public void Saving_keeps_the_settings_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Size(dialog).SelectedIndex = 1;
        CloseSettings(window, dialog, save: true);

        File.Exists(settingsPath).ShouldBeTrue();

        var next = Open(settingsPath);

        All<PreviewHost>(next).Single().Resolution.Width.ShouldBe(480);

        var again = OpenSettings(next);

        Size(again).SelectedIndex.ShouldBe(1);
    }

    // --- which preset the window opens on next (ADR-0093) --------------------

    private static Button StartupPreset(Visual within) => All<Button>(within).Single(c => c.Name == "defaultPreset");

    /// <summary>The name the Startup patch row shows.</summary>
    private static string? StartupName(Visual within) =>
        All<TextBlock>(within).Single(t => t.Name == "defaultPresetName").Text;

    /// <summary>The patches among the engine's presets, by name, in the order the gallery shows them.</summary>
    private static List<string> Patches =>
        [.. Presets.All.Where(p => p.Kind is not PresetKind.Blank).Select(p => p.Name)];

    /// <summary>Picks the startup patch the way a person would: the row's button, then a tile of the gallery it opens.</summary>
    private static void PickStartupPreset(MainWindow window, ModalOverlay dialog, string name)
    {
        StartupPreset(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Count() < 2; attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        All<Button>(window).Single(b => b.Name == "tile" && ((PatchPreset)b.Tag!).Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Count() > 1; attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    /// <summary>
    /// A machine with no settings file names the first preset that is a patch —
    /// which is not the first preset, the blank canvas heading the list.
    /// </summary>
    [AvaloniaFact]
    public void The_startup_preset_starts_on_the_first_patch()
    {
        var window = Open();

        StartupName(OpenSettings(window)).ShouldBe(Patches[0]);
    }

    /// <summary>What is picked here is a launch's business, not this one's.</summary>
    [AvaloniaFact]
    public void Picking_a_startup_preset_does_not_change_the_canvas()
    {
        var window = Open();
        var editor = Editor(window);
        var before = editor.History.Patch;

        var dialog = OpenSettings(window);

        PickStartupPreset(window, dialog, Patches[2]);

        StartupName(dialog).ShouldBe(Patches[2]);
        editor.History.Patch.ShouldBeSameAs(before);

        CloseSettings(window, dialog, save: true);

        editor.History.Patch.ShouldBeSameAs(before);
    }

    [AvaloniaFact]
    public void The_startup_preset_is_kept_and_opens_the_next_launch_on_it()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        var chosen = Patches[3];

        PickStartupPreset(window, dialog, chosen);
        CloseSettings(window, dialog, save: true);

        OutputSettings.Load(settingsPath).DefaultPreset.ShouldBe(chosen);

        var next = Open(settingsPath);

        next.Title.ShouldBe($"{chosen} — {Core.GlobalConstants.ApplicationName}");
        StartupName(OpenSettings(next)).ShouldBe(chosen);
    }

    /// <summary>Changed and not saved is dropped, like every other Graphics row.</summary>
    [AvaloniaFact]
    public void A_startup_preset_changed_and_not_saved_is_dropped()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        var before = StartupName(dialog);

        PickStartupPreset(window, dialog, Patches[4]);
        CloseSettings(window, dialog, save: false);

        File.Exists(settingsPath).ShouldBeFalse();

        StartupName(OpenSettings(window)).ShouldBe(before);
    }

    /// <summary>
    /// Saving some other setting leaves a startup patch this launch cannot
    /// offer as it was.
    /// </summary>
    /// <remarks>
    /// A plugin's preset chosen as the startup patch, and one launch without
    /// the plugin. The choice is listed all the same, so it is the row shown
    /// and what Save writes back, rather than the patch the window fell back
    /// to. The sound settings beside it are kept for a backend that is not
    /// installed this launch, for the same reason.
    /// </remarks>
    [AvaloniaFact]
    public void A_startup_patch_this_launch_does_not_offer_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{\"defaultPreset\":\"A Plugin's Preset\"}");

        OutputSettings.Load(settingsPath).DefaultPreset
            .ShouldBe("A Plugin's Preset", "the file is read the way this test expects");

        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        var rate = PreviewFrameRate(dialog);

        rate.SelectedIndex = rate.SelectedIndex == 1 ? 2 : 1;
        Settle(window);

        CloseSettings(window, dialog, save: true);

        OutputSettings.Load(settingsPath).DefaultPreset.ShouldBe("A Plugin's Preset");
    }

    // --- the recording and sound sections ------------------------------------

    private static ComboBox FrameRate(Visual within) => All<ComboBox>(within).Single(c => c.Name == "frameRate");

    private static NumericUpDown Quality(Visual within) => All<NumericUpDown>(within).Single(c => c.Name == "jpegQuality");

    private static ComboBox Latency(Visual within) => All<ComboBox>(within).Single(c => c.Name == "latency");

    private static ComboBox CountIn(Visual within) => All<ComboBox>(within).Single(c => c.Name == "countIn");

    private static CheckBox RewindFirst(Visual within) =>
        All<CheckBox>(within).Single(c => c.Name == "rewindBeforeTake");

    /// <summary>
    /// Both device tabs say where the device comes from, because everything
    /// either one configures is a plugin's, and neither tab otherwise names one.
    /// Nothing is loaded here, so what they say is the other half: that nothing
    /// plays and nothing is heard, rather than a tab of rows about nothing.
    /// </summary>
    [AvaloniaFact]
    public void The_device_tabs_say_where_the_device_comes_from()
    {
        var window = Open();
        var dialog = OpenSettings(window, SoundTab);

        Named<TextBlock>(dialog, "soundNote").Text
            .ShouldBe("No sound plugin is installed, so nothing plays. See About for where plugins are looked for.");

        Tabs(dialog).SelectedIndex = MidiTab;
        Settle(window);

        Named<TextBlock>(dialog, "midiNote").Text
            .ShouldBe("No MIDI plugin is installed, so the only instrument is the computer's own keyboard.");
    }

    /// <summary>
    /// With one installed, the sentence names the backend and then the plugin
    /// behind it — the plugin by both the name About lists it under and the id
    /// its folder goes by, so it can be found and taken away again.
    /// </summary>
    [AvaloniaFact]
    public void A_backend_is_named_with_the_plugin_that_offered_it()
    {
        OutputSections.Attributed("Played by WASAPI (shared mode)", new PluginInfo("win.io", "Windows sound and MIDI"))
            .ShouldBe("Played by WASAPI (shared mode), from the Windows sound and MIDI plugin (win.io).");

        OutputSections.Attributed("Played by WASAPI (shared mode)", null)
            .ShouldBe("Played by WASAPI (shared mode).");
    }

    [AvaloniaFact]
    public void Recording_and_sound_start_on_the_defaults()
    {
        var window = Open();

        var recording = OpenSettings(window, RecordingTab);

        (FrameRate(recording).SelectedItem as string).ShouldBe("30 fps");
        Quality(recording).Value.ShouldBe(85);
        (CountIn(recording).SelectedItem as string).ShouldBe("3 s");
        RewindFirst(recording).IsChecked.ShouldBe(true);

        Tabs(recording).SelectedIndex = SoundTab;
        Settle(window);

        (Latency(recording).SelectedItem as string).ShouldBe("30 ms");
    }

    [AvaloniaFact]
    public void Recording_and_sound_are_kept_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        FrameRate(dialog).SelectedIndex = 4;
        Quality(dialog).Value = 60;
        CountIn(dialog).SelectedIndex = 0;
        RewindFirst(dialog).IsChecked = false;

        Tabs(dialog).SelectedIndex = SoundTab;
        Settle(window);

        Latency(dialog).SelectedIndex = 0;

        CloseSettings(window, dialog, save: true);

        var kept = OutputSettings.Load(settingsPath);

        kept.FrameRate.ShouldBe(60);
        kept.JpegQuality.ShouldBe(60);
        kept.LatencyMilliseconds.ShouldBe(10);
        kept.CountInSeconds.ShouldBe(OutputSettings.NoCountIn);
        kept.RewindBeforeTake.ShouldBeFalse();

        var again = OpenSettings(Open(settingsPath), RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("60 fps");
        Quality(again).Value.ShouldBe(60);
        (CountIn(again).SelectedItem as string).ShouldBe("None");
        RewindFirst(again).IsChecked.ShouldBe(false);
    }

    [AvaloniaFact]
    public void Recording_and_sound_changes_are_dropped_without_save()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        FrameRate(dialog).SelectedIndex = 0;
        Quality(dialog).Value = 20;
        CountIn(dialog).SelectedIndex = 0;
        RewindFirst(dialog).IsChecked = false;

        CloseSettings(window, dialog, save: false);

        File.Exists(settingsPath).ShouldBeFalse();

        var again = OpenSettings(window, RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("30 fps");
        Quality(again).Value.ShouldBe(85);
        (CountIn(again).SelectedItem as string).ShouldBe("3 s");
        RewindFirst(again).IsChecked.ShouldBe(true);
    }

    // --- which encoder a take goes through (ADR-0089) ------------------------

    private static ComboBox VideoFormat(Visual within) => All<ComboBox>(within).Single(c => c.Name == "videoFormat");

    private static ComboBox SoundFormat(Visual within) => All<ComboBox>(within).Single(c => c.Name == "soundFormat");

    private static TextBox FfmpegBox(Visual within) => All<TextBox>(within).Single(c => c.Name == "ffmpeg");

    /// <summary>
    /// A window with no settings file of its own starts on the formats written
    /// here — which is also what keeps this test the same on a machine with an
    /// ffmpeg and one without.
    /// </summary>
    [AvaloniaFact]
    public void The_formats_start_on_the_ones_written_here()
    {
        var recording = OpenSettings(Open(), RecordingTab);

        (VideoFormat(recording).SelectedItem as string).ShouldBe(ClipFormats.MotionJpegAvi.Label);
        (SoundFormat(recording).SelectedItem as string).ShouldBe(ClipFormats.Wav.Label);
        FfmpegBox(recording).Text.ShouldBeNullOrEmpty();
    }

    /// <summary>Every format is offered, and only the ones of that kind.</summary>
    [AvaloniaFact]
    public void Each_picker_offers_its_own_list()
    {
        var recording = OpenSettings(Open(), RecordingTab);

        VideoFormat(recording).ItemsSource.ShouldBe(ClipFormats.Pictures.Select(f => f.Label));
        SoundFormat(recording).ItemsSource.ShouldBe(ClipFormats.Sounds.Select(f => f.Label));
    }

    [AvaloniaFact]
    public void The_formats_and_the_ffmpeg_are_kept_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        VideoFormat(dialog).SelectedIndex = ClipFormats.Pictures.ToList().IndexOf(ClipFormats.Vp9WebM);
        SoundFormat(dialog).SelectedIndex = ClipFormats.Sounds.ToList().IndexOf(ClipFormats.Mp3);

        // With a space on the end, which is what pasting one in leaves behind.
        FfmpegBox(dialog).Text = " /opt/ffmpeg ";

        CloseSettings(window, dialog, save: true);

        var kept = OutputSettings.Load(settingsPath);

        kept.VideoFormat.ShouldBe(ClipFormats.Vp9WebM.Id);
        kept.SoundFormat.ShouldBe(ClipFormats.Mp3.Id);
        kept.FfmpegPath.ShouldBe("/opt/ffmpeg");

        var again = OpenSettings(Open(settingsPath), RecordingTab);

        (VideoFormat(again).SelectedItem as string).ShouldBe(ClipFormats.Vp9WebM.Label);
        (SoundFormat(again).SelectedItem as string).ShouldBe(ClipFormats.Mp3.Label);
        FfmpegBox(again).Text.ShouldBe("/opt/ffmpeg");
    }

    [AvaloniaFact]
    public void A_format_changed_and_not_saved_is_dropped()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        var before = VideoFormat(dialog).SelectedItem as string;

        VideoFormat(dialog).SelectedIndex = ClipFormats.Pictures.Count - 1;

        CloseSettings(window, dialog, save: false);

        var again = OpenSettings(window, RecordingTab);

        (VideoFormat(again).SelectedItem as string).ShouldBe(before);
    }

    /// <summary>
    /// The one default that is a question about the machine: a new settings file
    /// starts on H.264 wherever there is an ffmpeg to write it, because the AVI
    /// is the fallback and not the preference — ADR-0089. Asked the same way the
    /// program asks, so this says the same thing on either kind of machine.
    /// </summary>
    [AvaloniaFact]
    public void A_new_settings_file_starts_on_the_best_format_the_machine_can_write()
    {
        var recording = OpenSettings(Open(settingsPath), RecordingTab);

        (VideoFormat(recording).SelectedItem as string)
            .ShouldBe(ClipFormats.Preferred(Ffmpeg.Resolve(null) is not null).Label);
    }

    /// <summary>Save and Cancel sit side by side, in that order, against the right-hand edge.</summary>
    [AvaloniaFact]
    public void Save_and_cancel_sit_together_on_the_right()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        var save = All<Button>(dialog).Single(b => b.Content as string == "Save");
        var cancel = All<Button>(dialog).Single(b => b.Content as string == "Cancel");

        var row = save.Parent.ShouldBeOfType<StackPanel>();

        cancel.Parent.ShouldBeSameAs(row);
        row.Children.IndexOf(save).ShouldBeLessThan(row.Children.IndexOf(cancel));
        row.HorizontalAlignment.ShouldBe(Avalonia.Layout.HorizontalAlignment.Right);

        var tabs = Tabs(dialog);
        var rightEdge = cancel.TranslatePoint(new Point(cancel.Bounds.Width, 0), tabs)!.Value.X;

        rightEdge.ShouldBe(tabs.Bounds.Width, 1, "the buttons end where the tabs above them end");
    }

    /// <summary>Cancel and the cross are every way out that is not Save, and change nothing.</summary>
    [AvaloniaTheory]
    [InlineData(Cross)]
    [InlineData("Cancel")]
    public void Closing_without_saving_puts_the_settings_back(string by)
    {
        var window = Open(settingsPath);
        var preview = All<PreviewHost>(window).Single();
        var before = preview.Resolution;

        var dialog = OpenSettings(window);

        Size(dialog).SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        preview.Resolution.ShouldBe(before, "nothing is in force until Save");

        CloseSettings(window, dialog, by);

        preview.Resolution.ShouldBe(before);
        File.Exists(settingsPath).ShouldBeFalse();

        var again = OpenSettings(window);

        Size(again).SelectedIndex.ShouldBe(3, "the size it opened on, not the one picked");
    }

    /// <summary>
    /// The assistant opens beside the canvas rather than under it, so the
    /// palette and the inspector keep their width while a conversation is
    /// going on. Both are the same height because they are the same row, and
    /// a resize of the window leaves the assistant's own width alone.
    /// </summary>
    [AvaloniaFact]
    public void The_assistant_shares_the_canvas_row()
    {
        var window = Open();

        var assistant = All<AssistantPanel>(window).Single();
        var editor = Editor(window);

        var toggle = Named<ToggleButton>(window, "assistant");

        toggle.IsChecked = true;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        assistant.IsVisible.ShouldBeTrue();
        assistant.Bounds.Height.ShouldBe(editor.Bounds.Height, 1);

        // Translated into the window's own coordinates, since the two are no
        // siblings: the assistant hangs off the window's grid and the editor off
        // the patch grid inside it.
        var assistantLeft = assistant.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("the assistant is not in this window");
        var editorLeft = editor.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        assistantLeft.X.ShouldBeLessThan(editorLeft.X, "it sits to the left of the patch");

        var widthBefore = assistant.Bounds.Width;

        window.Width += 200;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        assistant.Bounds.Width.ShouldBe(widthBefore, 1, "resizing the window grows the patch, not the assistant");
    }

    /// <summary>
    /// About is on the toolbar rather than in the Output's panel: it is about
    /// the program, and nothing there is about a patch at all.
    /// </summary>
    [AvaloniaFact]
    public void About_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Named<Button>(window, "about").ShouldNotBeNull();

        Select(window, Editor(window).History.Patch.Output);

        Named<Button>(window, "about").ShouldNotBeNull();
    }

    /// <summary>
    /// Three of the buttons are drawn rather than typed. A folder and a floppy
    /// disk are what open and save look like everywhere, and neither is a
    /// character any font here can be relied on to have — the code points exist,
    /// and on Windows they resolve to the color emoji font, which would put
    /// full-color pictures in a bar of thin gray strokes. Tidy is drawn for the
    /// opposite reason: no character means what it does, so it is a patch in
    /// miniature instead.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("tidy")]
    public void The_drawn_icons_are_drawn_rather_than_typed(string name)
    {
        var window = Open();
        var icon = Named<Button>(window, name).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();

        // Taken from the button rather than set here, so that hovering, pressing
        // and gray-out all reach it. A binding that failed to resolve leaves this
        // null and draws nothing at all.
        icon.Stroke.ShouldNotBeNull("the stroke follows the button's own foreground");
    }

    /// <summary>
    /// Every toolbar button is a symbol now, so the tip is the only place it
    /// says what it does. One without is a button nobody can identify.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("assistant")]
    [InlineData("settings")]
    [InlineData("about")]
    [InlineData("tidy")]
    [InlineData("record")]
    [InlineData("pause")]
    [InlineData("rewind")]
    public void Every_toolbar_icon_says_what_it_is(string name)
    {
        var window = Open();
        var button = Named<ContentControl>(window, name);

        var tip = ToolTip.GetTip(button) as string;

        tip.ShouldNotBeNullOrWhiteSpace();
        tip.ShouldNotBe(button.Content as string, "the tip is a sentence, not the glyph again");
        tip.Length.ShouldBeGreaterThan(8);
    }

    /// <summary>
    /// A size picked is a draft: the preview keeps the one it has until Save,
    /// and takes the new one then.
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_size_reaches_the_preview_only_on_save()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();
        var before = preview.Resolution;

        var dialog = OpenSettings(window);

        Size(dialog).SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        preview.Resolution.ShouldBe(before);

        CloseSettings(window, dialog, save: true);

        preview.Resolution.Width.ShouldBe(320);
    }

    /// <summary>
    /// A row picked in the size picker before it was grayed out is not what Save
    /// takes.
    /// </summary>
    /// <remarks>
    /// The picker is grayed out for the length of a take, whose file has
    /// committed to a size and drops every frame that arrives at another.
    /// Graying a box does not take back a row already picked in it — during the
    /// count-in, say — so Save does not read the box while it is gray, and puts
    /// its row back.
    /// </remarks>
    [AvaloniaFact]
    public void A_size_picked_while_the_picker_is_grayed_out_is_not_saved()
    {
        var window = Open(settingsPath);
        var preview = All<PreviewHost>(window).Single();
        var before = preview.Resolution;

        var dialog = OpenSettings(window);
        var size = Size(dialog);
        var row = size.SelectedIndex;

        // As a take finds it: gray, with a row picked that was never saved.
        size.IsEnabled = false;
        size.SelectedIndex = row == 0 ? 1 : 0;
        Settle(window);

        size.IsEnabled.ShouldBeFalse();
        size.SelectedIndex.ShouldNotBe(row, "a gray box still holds whatever row it is given");

        CloseSettings(window, dialog, save: true);

        var saved = OutputSettings.Load(settingsPath);

        saved.Width.ShouldBe(before.Width, "the size in force is the one written");
        saved.Height.ShouldBe(before.Height);
        preview.Resolution.ShouldBe(before, "and the preview keeps it");

        size.SelectedIndex.ShouldBe(row, "the box says the size in force again");
    }

    // --- the preview's own frame rate ------------------------------------

    private static ComboBox PreviewFrameRate(Visual within) =>
        All<ComboBox>(within).Single(c => c.Name == "previewFrameRate");

    /// <summary>Nothing has ever asked for a cap, so the preview starts uncapped.</summary>
    [AvaloniaFact]
    public void The_preview_rate_starts_unlimited()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();

        preview.FrameRate.ShouldBe(0);
        (PreviewFrameRate(OpenSettings(window)).SelectedItem as string).ShouldBe("Unlimited");
    }

    /// <summary>
    /// A cap picked is a draft, like every other Graphics row: the preview
    /// keeps running uncapped until Save, and takes the new rate then.
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_preview_rate_reaches_the_preview_only_on_save()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();

        var dialog = OpenSettings(window);

        PreviewFrameRate(dialog).SelectedIndex = 1; // 24 fps
        Dispatcher.UIThread.RunJobs();

        preview.FrameRate.ShouldBe(0, "a draft until Save");

        CloseSettings(window, dialog, save: true);

        preview.FrameRate.ShouldBe(24);
    }

    /// <summary>The cap is kept, the same way the recording rate is.</summary>
    [AvaloniaFact]
    public void The_preview_rate_is_kept_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        PreviewFrameRate(dialog).SelectedIndex = 3; // 30 fps
        CloseSettings(window, dialog, save: true);

        var kept = OutputSettings.Load(settingsPath);
        kept.PreviewFrameRate.ShouldBe(30);

        var again = OpenSettings(Open(settingsPath));
        (PreviewFrameRate(again).SelectedItem as string).ShouldBe("30 fps");
    }

    // --- the render switch and the interpreter -----------------------------------

    /// <summary>
    /// A choice between the two, named for what it would draw with either way.
    /// </summary>
    [AvaloniaFact]
    public void The_render_box_offers_the_gpu_and_the_cpu()
    {
        var window = Open();
        var render = All<ComboBox>(OpenSettings(window)).Single(b => b.Name == "render");

        render.ItemsSource.ShouldBe(new[] { "GPU", "CPU" });
    }

    /// <summary>
    /// Compiled and interpreted give the same bits, so which one runs is not a
    /// setting — only a flag a run is started with (ADR-0076).
    /// </summary>
    [AvaloniaFact]
    public void Whether_the_cpu_compiles_is_not_a_setting()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        All<ToggleButton>(dialog).ShouldNotContain(b => b.Content as string == "Compiled" || b.Content as string == "Interpreted");
        All<TextBlock>(dialog).Select(t => t.Text).ShouldNotContain("CPU code");
    }

    /// <summary>
    /// Started interpreted, a run never puts IL under a picture the CPU draws, and
    /// says so once rather than leaving the status bar's word to be noticed.
    /// </summary>
    [AvaloniaFact]
    public void A_run_started_interpreted_stays_interpreted_and_says_so()
    {
        var window = NewMainWindow(new EditorSetup { Interpreted = true });

        window.Show();
        Settle(window);

        var preview = All<PreviewHost>(window).Single();

        preview.Use(PreviewBackend.Cpu);
        Settle(window);

        preview.Program.Il.ShouldBeNull();
        All<ReportLine>(window).Single().History.ShouldContain(line => line.Contains("interpreted"));
    }

    /// <summary>
    /// The one control whose draft could reach the picture on its own, so pinned
    /// by itself: turning the GPU off without saving leaves the shader asked for.
    /// </summary>
    [AvaloniaFact]
    public void The_gpu_switch_changes_nothing_until_save()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();
        var wanted = preview.Wanted;

        var dialog = OpenSettings(window);
        var gpu = All<ComboBox>(dialog).Single(b => b.Name == "render");

        if (!gpu.IsEnabled) return;

        gpu.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        preview.Wanted.ShouldBe(wanted);

        CloseSettings(window, dialog, save: false);

        preview.Wanted.ShouldBe(wanted);
    }

    // --- the record button ---------------------------------------------------

    /// <summary>
    /// Named rather than found by content: it lives on the toolbar now and a
    /// glyph, unlike the label it replaced, has nothing a test can read.
    /// </summary>
    private static Button Record(MainWindow window) => Named<Button>(window, "record");

    /// <summary>
    /// On the toolbar, so it is reachable with nothing selected — unlike the
    /// Output panel row it replaced (ADR-0080).
    /// </summary>
    [AvaloniaFact]
    public void The_record_button_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Record(window).ShouldNotBeNull();

        Select(window, Editor(window).History.Patch.Output);

        Record(window).ShouldNotBeNull();
    }

    /// <summary>
    /// Filled rather than outlined, unlike the other drawn icons — a record
    /// light is a dot, not a stroke, and reads at this size only solid.
    /// </summary>
    [AvaloniaFact]
    public void The_record_glyph_is_filled_rather_than_stroked()
    {
        var window = Open();
        var icon = Record(window).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();
        icon.Fill.ShouldNotBeNull("the fill follows the button's own foreground");
    }

    // --- the rewind button -----------------------------------------------

    /// <summary>
    /// Named rather than found by content, same as record: it lives on the
    /// toolbar now and a glyph has nothing a test can read.
    /// </summary>
    private static Button Rewind(MainWindow window) => Named<Button>(window, "rewind");

    /// <summary>
    /// On the toolbar, so it is reachable with nothing selected — unlike the
    /// Output panel row it replaced (ADR-0081).
    /// </summary>
    [AvaloniaFact]
    public void The_rewind_button_is_on_the_toolbar_whatever_is_selected()
    {
        var window = Open();

        Rewind(window).ShouldNotBeNull();

        Select(window, Editor(window).History.Patch.Output);

        Rewind(window).ShouldNotBeNull();
    }

    /// <summary>
    /// Filled rather than outlined, like record beside it — a bar and a
    /// triangle read at this size only solid.
    /// </summary>
    [AvaloniaFact]
    public void The_rewind_glyph_is_filled_rather_than_stroked()
    {
        var window = Open();
        var icon = Rewind(window).Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        icon.Data.ShouldNotBeNull();
        icon.Fill.ShouldNotBeNull("the fill follows the button's own foreground");
    }

    /// <summary>The preset it opens on draws something, so there is a take to record.</summary>
    [AvaloniaFact]
    public void The_record_button_is_offered_when_the_patch_reaches_something()
    {
        var window = Open();
        Select(window, Editor(window).History.Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing wired into either half of the Output means nothing to record, and
    /// the button says so by being grayed rather than by opening a dialog with
    /// an empty list of file types.
    /// </summary>
    [AvaloniaFact]
    public void The_record_button_is_grayed_out_when_the_patch_reaches_nothing()
    {
        var window = Open();
        var editor = Editor(window);

        editor.History.Open(Presets.Empty(NodeCatalog.BuiltIn));
        Select(window, editor.History.Patch.Output);

        Record(window).IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// And it comes back the moment something reaches it — the state follows the
    /// patch rather than being decided once when the panel was built.
    /// </summary>
    [AvaloniaFact]
    public void Wiring_something_up_brings_the_record_button_back()
    {
        var window = Open();
        var editor = Editor(window);

        editor.History.Open(Presets.Empty(NodeCatalog.BuiltIn));
        Select(window, editor.History.Patch.Output);
        Record(window).IsEnabled.ShouldBeFalse();

        var knob = editor.Edits.AddNode("value");
        knob.ShouldNotBeNull();
        editor.History.Patch.Connect(knob.Id, 0, editor.History.Patch.Output.Id, NodeCatalog.OutputColorPort);
        editor.History.Record();

        Select(window, editor.History.Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }

    // --- the count-in --------------------------------------------------------

    /// <summary>What the status bar has said, newest last.</summary>
    private static IReadOnlyList<string> Said(MainWindow window) =>
        All<ReportLine>(window).Single().History;

    /// <summary>Where a take under test would go, which is nowhere a machine keeps anything.</summary>
    private static string TakePath(string extension) => Path.Combine(
        Path.GetTempPath(),
        $"flyback-take-{Guid.NewGuid():N}{extension}");

    /// <summary>
    /// A step no test waits out, for the count that is meant to be interrupted
    /// rather than finished.
    /// </summary>
    private static readonly TimeSpan Unhurried = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The numbers go on the status bar, where everything else this program has
    /// to say goes — there is no second place for them to appear.
    /// </summary>
    [AvaloniaFact]
    public async Task The_count_says_how_many_seconds_are_left()
    {
        var window = Open();
        var path = TakePath(ClipFormats.MotionJpegAvi.Extension);

        var counting = window.Recording.CountInAsync(path, Unhurried);
        Settle(window);

        Said(window)[^1].ShouldBe($"Recording {Path.GetFileName(path)} in 3…");

        Record(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await counting;
    }

    /// <summary>
    /// The count is the one part of a take that can be called off, since no file
    /// has been opened yet — and the button that starts it is what calls it off.
    /// </summary>
    [AvaloniaFact]
    public async Task The_count_can_be_called_off_before_the_take_starts()
    {
        var window = Open();
        var path = TakePath(ClipFormats.MotionJpegAvi.Extension);

        var counting = window.Recording.CountInAsync(path, Unhurried);
        Settle(window);

        var button = Record(window);

        button.IsEnabled.ShouldBeTrue("or the count could not be called off");
        (ToolTip.GetTip(button) as string).ShouldNotBeNull().ShouldContain("Call off the count");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await counting;

        Settle(window);

        Said(window)[^1].ShouldBe($"{Path.GetFileName(path)} was not recorded.");
        File.Exists(path).ShouldBeFalse("nothing was ever opened");
        (ToolTip.GetTip(button) as string).ShouldNotBeNull().ShouldContain("Record what the patch is doing");
    }

    /// <summary>
    /// Closing the window calls a count-in off, unsaved work or none (ADR-0090).
    /// </summary>
    /// <remarks>
    /// With something to lose the close puts its question up, which can stay up
    /// for as long as it likes. The count is called off before anything is
    /// asked, so it never runs on underneath to start a take behind a modal
    /// question.
    /// </remarks>
    [AvaloniaFact]
    public async Task Closing_with_unsaved_work_calls_a_count_in_off()
    {
        var window = Open();

        Editor(window).Edits.AddNode("value").ShouldNotBeNull();

        var counting = window.Recording.CountInAsync(TakePath(ClipFormats.MotionJpegAvi.Extension), Unhurried);
        Settle(window);

        counting.IsCompleted.ShouldBeFalse("the count is under way");

        window.Close();

        await Task.WhenAny(counting, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Settle(window);

        counting.IsCompleted.ShouldBeTrue("the close called the count off before asking about anything");
    }

    /// <summary>
    /// What the count is for: the take starts at nought seconds, so a recording
    /// begins where the patch does rather than wherever the session had got to.
    /// </summary>
    /// <remarks>
    /// A <c>.wav</c> with the sound stopped, which is what a headless test has:
    /// the take is refused as it opens, which is the proof the count ran through
    /// to starting one — and leaves no file to close.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_count_takes_the_patch_back_to_zero_before_the_take_starts()
    {
        var window = Open();
        var preview = All<PreviewHost>(window).Single();

        preview.Time = 30;

        await window.Recording.CountInAsync(TakePath(ClipFormats.Wav.Extension), TimeSpan.Zero);

        Settle(window);

        preview.Time.ShouldBeLessThan(1);
        Said(window).ShouldContain(line => line.Contains("Turn the Output's Volume up"));
    }

    /// <summary>
    /// A step long enough that a count which was meant to be off would be caught
    /// waiting one out, and never waited at all when it is.
    /// </summary>
    private static readonly TimeSpan Noticeable = TimeSpan.FromSeconds(2);

    /// <summary>
    /// No count-in starts the take on the press, with nothing counted on the bar
    /// — what the picker's first row is for (ADR-0091).
    /// </summary>
    [AvaloniaFact]
    public async Task A_count_in_of_none_starts_the_take_at_once()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        CountIn(dialog).SelectedIndex = 0;
        CloseSettings(window, dialog, save: true);

        await window.Recording.CountInAsync(TakePath(ClipFormats.Wav.Extension), Noticeable);

        Settle(window);

        Said(window).ShouldNotContain(line => line.Contains(" in 1"), "nothing was counted");
        Said(window).ShouldContain(line => line.Contains("Turn the Output's Volume up"), "the take was tried");
    }

    /// <summary>
    /// The rewind switched off leaves the clock where the session had got to, for
    /// recording something a patch has already arrived at (ADR-0091).
    /// </summary>
    [AvaloniaFact]
    public async Task The_rewind_switched_off_leaves_the_clock_alone()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        RewindFirst(dialog).IsChecked = false;
        CloseSettings(window, dialog, save: true);

        var preview = All<PreviewHost>(window).Single();

        preview.Time = 30;

        await window.Recording.CountInAsync(TakePath(ClipFormats.Wav.Extension), TimeSpan.Zero);

        Settle(window);

        preview.Time.ShouldBeGreaterThan(29);
        Said(window).ShouldContain(line => line.Contains("Turn the Output's Volume up"), "the take was tried");
    }

    /// <summary>A grayed control that will not say why is worse than no control.</summary>
    [AvaloniaFact]
    public void The_grayed_record_button_says_why()
    {
        var window = Open();
        var editor = Editor(window);

        editor.History.Open(Presets.Empty(NodeCatalog.BuiltIn));
        Select(window, editor.History.Patch.Output);

        var button = Record(window);

        ToolTip.GetShowOnDisabled(button).ShouldBeTrue("or the reason is never read");
        (ToolTip.GetTip(button) as string).ShouldNotBeNull().ShouldContain("nothing to record");
    }

    /// <summary>A sequencer gets its own list, and the Output does not.</summary>
    [AvaloniaFact]
    public void Only_the_output_gets_the_output_settings()
    {
        var window = Open();
        var editor = Editor(window);

        var sequencer = editor.Edits.AddNode("seq.notes");
        sequencer.ShouldNotBeNull();

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        ShowingSettings(window).ShouldBeFalse("a sequencer is not the Output");
        All<TextBlock>(window).Select(t => t.Text).ShouldContain("A3", "but it does get its notes");
    }

    /// <summary>
    /// An emptied Quality box keeps what was saved, and says so: the controls are
    /// kept between openings, so a blank left in one is what the next opening shows.
    /// </summary>
    [AvaloniaFact]
    public void An_emptied_quality_box_says_what_it_kept()
    {
        var window = Open();

        var dialog = OpenSettings(window, RecordingTab);

        Quality(dialog).Value = null;
        CloseSettings(window, dialog, save: true);

        Quality(OpenSettings(window, RecordingTab)).Value.ShouldBe(85);
    }
}
