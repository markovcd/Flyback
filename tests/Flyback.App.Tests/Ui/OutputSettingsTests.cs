using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Output settings — size, renderer, processor — which live in the settings
/// window and are kept between launches (ADR-0082), and the toolbar that opens it.
/// </summary>
/// <remarks>
/// The controls are the state of the instrument rather than of a dialog, so they are
/// made once and lent to the window each time it opens — and a control may have one
/// parent. Opening it twice is the sequence that throws if the window ever stops
/// being taken apart first.
/// </remarks>
public class OutputSettingsTests : UiTest, IDisposable
{
    /// <summary>Where a window under test keeps its settings, so none land in the machine's own.</summary>
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-output-settings-" + Guid.NewGuid().ToString("N"),
        "output.json");

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The real window, on the preset it opens with. Nothing is stubbed: with no
    /// plugins loaded the catalogue is empty and the audio device is silent,
    /// which is the same path a machine with no sound backend takes.
    /// </summary>
    private static MainWindow Open(string? settingsPath = null)
    {
        var window = new MainWindow(outputSettingsPath: settingsPath);

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
    private static T Named<T>(MainWindow window, string name)
        where T : Control =>
        All<T>(window).Single(c => c.Name == name);

    /// <summary>What every button in the window is labelled, in tree order.</summary>
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
    /// <paramref name="tab"/> — the Output tab unless told otherwise.
    /// </summary>
    private static ModalOverlay OpenSettings(MainWindow window, int tab = OutputTab)
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

    private const int OutputTab = 1, RecordingTab = 2, SoundTab = 3;

    /// <summary>Answers the settings window by its Save, or by its cross.</summary>
    private static void CloseSettings(MainWindow window, ModalOverlay dialog, bool save)
    {
        All<Button>(dialog)
            .Single(b => save ? b.Content as string == "Save" : b.Name == "dismiss")
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

        Select(window, Editor(window).Patch.Output);

        ShowingSettings(window).ShouldBeFalse();
    }

    /// <summary>
    /// A tab a section, opening on the agent's, and one Save under them all —
    /// switching tabs is not saving, and Save keeps the tabs not showing as well.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_window_has_a_tab_for_each_section()
    {
        var window = Open();
        var dialog = OpenSettings(window, tab: 0);
        var tabs = Tabs(dialog);

        tabs.Items.Cast<TabItem>().Select(t => (t.Header as TextBlock)?.Text)
            .ShouldBe(["Agent settings", "Output settings", "Recording settings", "Sound settings"]);
        tabs.SelectedIndex.ShouldBe(0);
        tabs.TabStripPlacement.ShouldBe(Dock.Left, "the sections are a list down the left");
        ShowingSettings(dialog).ShouldBeFalse("the Output tab is not the one showing");

        var frame = All<Border>(dialog).Single(b => b.Name == "dialog");
        var size = frame.Bounds.Size;

        tabs.SelectedIndex = OutputTab;
        Settle(window);

        ShowingSettings(dialog).ShouldBeTrue();

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
        Processor(dialog).IsChecked = false;
        CloseSettings(window, dialog, save: true);

        File.Exists(settingsPath).ShouldBeTrue();

        var next = Open(settingsPath);

        All<PreviewHost>(next).Single().Resolution.Width.ShouldBe(480);

        var again = OpenSettings(next);

        Size(again).SelectedIndex.ShouldBe(1);
        Processor(again).IsChecked.ShouldBe(false);
    }

    // --- the recording and sound sections ------------------------------------

    private static ComboBox FrameRate(Visual within) => All<ComboBox>(within).Single(c => c.Name == "frameRate");

    private static NumericUpDown Quality(Visual within) => All<NumericUpDown>(within).Single(c => c.Name == "jpegQuality");

    private static ComboBox Latency(Visual within) => All<ComboBox>(within).Single(c => c.Name == "latency");

    [AvaloniaFact]
    public void Recording_and_sound_start_on_the_defaults()
    {
        var window = Open();

        var recording = OpenSettings(window, RecordingTab);

        (FrameRate(recording).SelectedItem as string).ShouldBe("30 fps");
        Quality(recording).Value.ShouldBe(85);

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

        Tabs(dialog).SelectedIndex = SoundTab;
        Settle(window);

        Latency(dialog).SelectedIndex = 0;

        CloseSettings(window, dialog, save: true);

        var kept = OutputSettings.Load(settingsPath);

        kept.FrameRate.ShouldBe(60);
        kept.JpegQuality.ShouldBe(60);
        kept.LatencyMilliseconds.ShouldBe(10);

        var again = OpenSettings(Open(settingsPath), RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("60 fps");
        Quality(again).Value.ShouldBe(60);
    }

    [AvaloniaFact]
    public void Recording_and_sound_changes_are_dropped_without_save()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        FrameRate(dialog).SelectedIndex = 0;
        Quality(dialog).Value = 20;

        CloseSettings(window, dialog, save: false);

        File.Exists(settingsPath).ShouldBeFalse();

        var again = OpenSettings(window, RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("30 fps");
        Quality(again).Value.ShouldBe(85);
    }

    /// <summary>The cross is every way out that is not Save, and changes nothing.</summary>
    [AvaloniaFact]
    public void Closing_without_saving_puts_the_settings_back()
    {
        var window = Open(settingsPath);
        var preview = All<PreviewHost>(window).Single();
        var before = preview.Resolution;

        var dialog = OpenSettings(window);

        Size(dialog).SelectedIndex = 0;
        Processor(dialog).IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        preview.Resolution.ShouldBe(before, "nothing is in force until Save");

        CloseSettings(window, dialog, save: false);

        preview.Resolution.ShouldBe(before);
        File.Exists(settingsPath).ShouldBeFalse();

        var again = OpenSettings(window);

        Processor(again).IsChecked.ShouldBe(true);
    }

    /// <summary>
    /// The assistant opens under the canvas rather than across the window, so
    /// the palette and the inspector keep their height while a conversation is
    /// going on. Both are the same width because they are the same column.
    /// </summary>
    [AvaloniaFact]
    public void The_assistant_shares_the_canvas_column()
    {
        var window = Open();

        var assistant = All<AssistantPanel>(window).Single();
        var editor = Editor(window);

        assistant.GetVisualParent().ShouldBeSameAs(
            editor.GetVisualParent(),
            "the two are stacked in one column, not one above the whole window");

        var toggle = Named<ToggleButton>(window, "assistant");

        toggle.IsChecked = true;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        assistant.IsVisible.ShouldBeTrue();
        assistant.Bounds.Width.ShouldBe(editor.Bounds.Width, 1);
        assistant.Bounds.Width.ShouldBeLessThan(window.Bounds.Width - 200, "the side panels are still beside it");
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

        Select(window, Editor(window).Patch.Output);

        Named<Button>(window, "about").ShouldNotBeNull();
    }

    /// <summary>
    /// Three of the buttons are drawn rather than typed. A folder and a floppy
    /// disk are what open and save look like everywhere, and neither is a
    /// character any font here can be relied on to have — the code points exist,
    /// and on Windows they resolve to the color emoji font, which would put
    /// full-color pictures in a bar of thin grey strokes. Tidy is drawn for the
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
        // and grey-out all reach it. A binding that failed to resolve leaves this
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

    // --- the processor switch ----------------------------------------------

    private static ToggleButton Processor(Visual within) =>
        All<ToggleButton>(within).Single(b => b.Content as string is "Compiled" or "Interpreted");

    /// <summary>
    /// On by default, like the GPU beside it: a program is interpreted until its
    /// IL is ready anyway, so being on never makes anything wait.
    /// </summary>
    [AvaloniaFact]
    public void The_processor_starts_compiled_and_says_so()
    {
        var window = Open();
        var toggle = Processor(OpenSettings(window));

        toggle.IsChecked.ShouldBe(true);
        toggle.Content.ShouldBe("Compiled");
        (ToolTip.GetTip(toggle) as string).ShouldNotBeNull().ShouldContain("sound");
    }

    /// <summary>
    /// Off, once saved, puts the interpreter back under the picture at once —
    /// not at the next edit — because it is how the two are compared. The label
    /// follows the switch before that, since it says what Save would do.
    /// </summary>
    [AvaloniaFact]
    public void Saving_interpreted_takes_the_il_off_the_picture()
    {
        var window = Open();
        var dialog = OpenSettings(window);
        var toggle = Processor(dialog);

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        toggle.Content.ShouldBe("Interpreted");

        CloseSettings(window, dialog, save: true);

        All<PreviewHost>(window).Single().Program.Il.ShouldBeNull();

        var again = OpenSettings(window);

        Processor(again).IsChecked.ShouldBe(false);
        Processor(again).Content.ShouldBe("Interpreted");
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
        var gpu = All<ToggleButton>(dialog).Single(b => b.Content as string == "GPU");

        if (!gpu.IsEnabled) return;

        gpu.IsChecked = false;
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

        Select(window, Editor(window).Patch.Output);

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

        Select(window, Editor(window).Patch.Output);

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
        Select(window, Editor(window).Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing wired into either half of the Output means nothing to record, and
    /// the button says so by being greyed rather than by opening a dialog with
    /// an empty list of file types.
    /// </summary>
    [AvaloniaFact]
    public void The_record_button_is_greyed_out_when_the_patch_reaches_nothing()
    {
        var window = Open();
        var editor = Editor(window);

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);

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

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);
        Record(window).IsEnabled.ShouldBeFalse();

        var knob = editor.AddNode("value");
        knob.ShouldNotBeNull();
        editor.Patch.Connect(knob.Id, 0, editor.Patch.Output.Id, NodeCatalog.OutputColorPort);
        editor.NotifyPatchChanged();

        Select(window, editor.Patch.Output);

        Record(window).IsEnabled.ShouldBeTrue();
    }

    /// <summary>A greyed control that will not say why is worse than no control.</summary>
    [AvaloniaFact]
    public void The_greyed_record_button_says_why()
    {
        var window = Open();
        var editor = Editor(window);

        editor.Patch = Presets.Empty(NodeCatalog.BuiltIn);
        Select(window, editor.Patch.Output);

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

        var sequencer = editor.AddNode("seq.notes");
        sequencer.ShouldNotBeNull();

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        ShowingSettings(window).ShouldBeFalse("a sequencer is not the Output");
        All<TextBlock>(window).Select(t => t.Text).ShouldContain("A3", "but it does get its notes");
    }
}
