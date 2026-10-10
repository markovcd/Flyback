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

public partial class OutputSettingsTests
{
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

        CloseSettings(window, dialog);

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

        CloseSettings(window, dialog, "cancel");

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

        var save = Named<Button>(dialog, "save");
        var cancel = Named<Button>(dialog, "cancel");

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
    [InlineData("cancel")]
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

        CloseSettings(window, dialog);

        preview.Resolution.Width.ShouldBe(320);
    }

    /// <summary>
    /// A row picked in the size picker before it was grayed out is not what Save
    /// takes.
    /// </summary>
    /// <summary>A take's file has committed to a size, so the size picker is grayed out while one runs and given back when it ends.</summary>
    [AvaloniaFact]
    public void A_running_take_grays_out_the_size()
    {
        var window = Open();
        var take = Service<RecordingState>(window);
        var reactions = Service<Reactions>(window);
        var dialog = OpenSettings(window);

        take.SetRunning(true);
        reactions.Raise(new TakeMarked());

        Size(dialog).IsEnabled.ShouldBeFalse();

        take.SetRunning(false);
        reactions.Raise(new TakeMarked());

        Size(dialog).IsEnabled.ShouldBeTrue();
    }

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

        CloseSettings(window, dialog);

        var saved = OutputSettings.Load(settingsPath);

        saved.Width.ShouldBe(before.Width, "the size in force is the one written");
        saved.Height.ShouldBe(before.Height);
        preview.Resolution.ShouldBe(before, "and the preview keeps it");

        size.SelectedIndex.ShouldBe(row, "the box says the size in force again");
    }
}
