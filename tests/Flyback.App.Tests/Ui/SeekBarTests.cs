using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.App.Bars;
using Flyback.App.Controls;
using Flyback.App.Windows;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The seek bar on the toolbar: dragging it moves the patch's clock, paused or not; it
/// spans the patch's own length, typed beside it as an edit; and at its end the patch
/// stops, or comes round to zero where it loops.
/// </summary>
public sealed class SeekBarTests : UiTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-seek-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    public override void Dispose()
    {
        base.Dispose();

        if (Path.GetDirectoryName(settingsPath) is { } folder && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static SeekTrack Track(MainWindow window) => All<SeekTrack>(window).Single(s => s.Name == "seek");

    private static TextBox Length(MainWindow window) => All<TextBox>(window).Single(b => b.Name == "seekLength");

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).First();

    private static void TypeLength(MainWindow window, string text)
    {
        var box = Length(window);

        box.Focus();
        box.Text = text;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
    }

    /// <summary>Presses the strip where <paramref name="seconds"/> falls along it.</summary>
    private static void Click(MainWindow window, SeekTrack track, double seconds)
    {
        var at = track.TranslatePoint(track.At(seconds), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    /// <summary>A window and the seek bar in it, caught as the container builds it, on a patch <paramref name="length"/> long where given.</summary>
    private (MainWindow Window, SeekBar Bar) WithBar(EditorSetup? setup = null, double? length = null)
    {
        SeekBar? bar = null;
        var window = Open(setup: setup, replace: services => services.AddSingleton(sp => bar = ActivatorUtilities.CreateInstance<SeekBar>(sp)));

        if (length is not null) Lasting(window, length);

        return (window, bar.ShouldNotBeNull());
    }

    /// <summary>Gives the open patch a length, as an edit.</summary>
    private static void Lasting(MainWindow window, double? seconds)
    {
        Editor(window).History.Patch.Length = seconds;
        Editor(window).History.Record();
        Settle(window);
    }

    [AvaloniaFact]
    public void Clicking_along_the_bar_moves_the_clock()
    {
        var window = Open();

        Click(window, Track(window), 30);

        Preview(window).Time.ShouldBe(30, 1);
    }

    [AvaloniaFact]
    public void A_paused_patch_moved_along_the_bar_stays_paused_there()
    {
        var window = Open();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        Click(window, Track(window), 45);

        Service<Playback>(window).Paused.ShouldBeTrue();
        Preview(window).Time.ShouldBe(45, 1);
        Preview(window).Clock.ShouldNotBeNull().Invoke().ShouldBe(Preview(window).Time);
    }

    [AvaloniaFact]
    public void A_patch_that_says_no_length_plays_for_three_minutes()
    {
        var window = Open();

        Track(window).Maximum.ShouldBe(Patch.DefaultLength);
        Length(window).Text.ShouldBeEmpty();
        Length(window).PlaceholderText.ShouldBe("3:00.00");
    }

    [AvaloniaFact]
    public void The_bar_spans_the_length_the_patch_says()
    {
        var window = Open();

        Lasting(window, 150.5);

        Track(window).Maximum.ShouldBe(150.5);
        Length(window).Text.ShouldBe("2:30.50");
    }

    [AvaloniaFact]
    public void A_length_typed_to_the_hundredth_is_the_patchs_and_is_taken_back_like_any_edit()
    {
        var window = Open();

        TypeLength(window, "1:30.25");

        Editor(window).History.Patch.Length.ShouldBe(90.25);
        Editor(window).History.IsModified.ShouldBeTrue();
        Track(window).Maximum.ShouldBe(90.25);
        Length(window).Text.ShouldBe("1:30.25");

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);

        Editor(window).History.Patch.Length.ShouldBeNull();
        Track(window).Maximum.ShouldBe(Patch.DefaultLength);
        Length(window).Text.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Emptying_the_box_takes_the_length_away_and_is_taken_back_like_any_edit()
    {
        var window = Open();
        Lasting(window, 150.5);

        TypeLength(window, "");

        Editor(window).History.Patch.Length.ShouldBeNull();
        Track(window).Maximum.ShouldBe(Patch.DefaultLength);
        Length(window).Text.ShouldBeEmpty();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);

        Editor(window).History.Patch.Length.ShouldBe(150.5);
        Length(window).Text.ShouldBe("2:30.50");
    }

    /// <summary>On a canvas the text owns, emptying the box takes the length's line out of the text.</summary>
    [AvaloniaFact]
    public void Emptying_the_box_for_a_patch_the_text_owns_takes_its_line_out()
    {
        var window = Open();

        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var text = All<AvaloniaEdit.TextEditor>(window).Single(e => e.Name == "source");
        text.Text = "description \"A hum.\"\nlength 0:45.50\n\nt |> sine(freq: 220) |> out.left\n";
        All<Button>(window).Single(b => b.Name == "apply")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        TypeLength(window, "");

        text.Text.ShouldNotContain("length");
        Editor(window).History.Patch.Length.ShouldBeNull();
    }

    /// <summary>On a canvas the text owns, the length typed is written into the text as its line.</summary>
    [AvaloniaFact]
    public void A_length_typed_for_a_patch_the_text_owns_is_written_into_the_text()
    {
        var window = Open();

        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        var text = All<AvaloniaEdit.TextEditor>(window).Single(e => e.Name == "source");
        text.Text = "description \"A hum.\"\n\nt |> sine(freq: 220) |> out.left\n";
        All<Button>(window).Single(b => b.Name == "apply")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        TypeLength(window, "45.5");

        text.Text.ShouldStartWith("description \"A hum.\"\nlength 0:45.50\n\n");
        Editor(window).History.Patch.Length.ShouldBe(45.5);
    }

    [AvaloniaFact]
    public void A_length_that_cannot_be_read_puts_the_old_one_back()
    {
        var window = Open();

        TypeLength(window, "soon");

        Track(window).Maximum.ShouldBe(Patch.DefaultLength);
        Length(window).Text.ShouldBeEmpty();
        Editor(window).History.IsModified.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void An_unlooped_patch_stops_at_the_end_of_its_length()
    {
        var (window, bar) = WithBar(length: 20);

        Preview(window).Time = 21;
        bar.Update();
        Settle(window);

        Service<Playback>(window).Paused.ShouldBeTrue();
        Track(window).Value.ShouldBe(20);
    }

    [AvaloniaFact]
    public void Playing_a_patch_stopped_at_its_end_starts_it_from_zero()
    {
        var (window, bar) = WithBar(length: 20);

        Preview(window).Time = 21;
        bar.Update();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        Service<Playback>(window).Paused.ShouldBeFalse();
        Preview(window).Time.ShouldBeLessThan(1);
    }

    [AvaloniaFact]
    public void A_looped_patch_at_the_end_of_its_length_comes_round_to_zero()
    {
        var (window, bar) = WithBar();

        bar.Loop.IsChecked = true;
        Preview(window).Time = Patch.DefaultLength + 1;
        bar.Update();

        Service<Playback>(window).Paused.ShouldBeFalse();
        Preview(window).Time.ShouldBeLessThan(1);
        Track(window).Value.ShouldBeLessThan(1);
    }

    [AvaloniaFact]
    public void A_paused_patch_held_past_the_end_stays_where_it_is_held()
    {
        var (window, bar) = WithBar();

        bar.Loop.IsChecked = true;
        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        Click(window, Track(window), Patch.DefaultLength);
        bar.Update();

        Preview(window).Time.ShouldBe(Patch.DefaultLength, 1);
    }

    [AvaloniaFact]
    public void Looping_is_kept_for_the_next_window()
    {
        var setup = new EditorSetup { Folders = new() { SettingsPath = settingsPath } };
        var (_, bar) = WithBar(setup);

        bar.Loop.IsChecked.ShouldBe(false);
        bar.Loop.IsChecked = true;

        WithBar(setup).Bar.Loop.IsChecked.ShouldBe(true);
    }

    [AvaloniaFact]
    public void The_arrow_keys_step_a_focused_bar_and_End_takes_it_to_the_end()
    {
        var window = Open();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);
        Click(window, Track(window), 30);

        Track(window).Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Settle(window);

        Preview(window).Time.ShouldBe(30 + SeekTrack.Step, 1);

        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.End, RawInputModifiers.None);
        Settle(window);

        Preview(window).Time.ShouldBe(Track(window).Maximum, 1);
    }

    [AvaloniaFact]
    public void A_mouse_over_the_bar_is_told_the_time_under_it()
    {
        var window = Open();
        var track = Track(window);

        window.MouseMove(track.TranslatePoint(track.At(60), window)!.Value);
        Settle(window);

        (ToolTip.GetTip(track) as string).ShouldNotBeNull().ShouldStartWith("1:00.");
    }

    [AvaloniaFact]
    public void The_row_shows_where_the_clock_is()
    {
        var window = Open();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);
        Click(window, Track(window), 75);

        All<TextBlock>(window).Single(t => t.Name == "seekPosition").Text.ShouldStartWith("1:15.");
    }

    [AvaloniaFact]
    public void Clicking_along_the_bar_leaves_the_keys_with_the_canvas()
    {
        var window = Open();

        Editor(window).Focus();
        Click(window, Track(window), 30);

        Editor(window).IsFocused.ShouldBeTrue();
    }
}
