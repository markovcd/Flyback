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

        Press(Record(window));
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

        Press(button);
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
        CloseSettings(window, dialog);

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
        Play(window);
        var dialog = OpenSettings(window, RecordingTab);

        RewindFirst(dialog).IsChecked = false;
        CloseSettings(window, dialog);

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
        CloseSettings(window, dialog);

        Quality(OpenSettings(window, RecordingTab)).Value.ShouldBe(85);
    }
}
