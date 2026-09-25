using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Bars;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

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
        "canvas.json");

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

        window.Paused.ShouldBeTrue();
        Preview(window).Time.ShouldBe(45, 1);
        Preview(window).Clock.ShouldNotBeNull().Invoke().ShouldBe(Preview(window).Time);
    }

    [AvaloniaFact]
    public void A_patch_that_says_no_length_plays_for_three_minutes()
    {
        var window = Open();

        Track(window).Maximum.ShouldBe(Patch.DefaultLength);
        Length(window).Text.ShouldBe("3:00.00");
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
        Length(window).Text.ShouldBe("3:00.00");
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
        Length(window).Text.ShouldBe("3:00.00");
        Editor(window).History.IsModified.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void An_unlooped_patch_stops_at_the_end_of_its_length()
    {
        var (window, bar) = WithBar(length: 20);

        Preview(window).Time = 21;
        bar.Update();
        Settle(window);

        window.Paused.ShouldBeTrue();
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

        window.Paused.ShouldBeFalse();
        Preview(window).Time.ShouldBeLessThan(1);
    }

    [AvaloniaFact]
    public void A_looped_patch_at_the_end_of_its_length_comes_round_to_zero()
    {
        var (window, bar) = WithBar();

        bar.Loop.IsChecked = true;
        Preview(window).Time = Patch.DefaultLength + 1;
        bar.Update();

        window.Paused.ShouldBeFalse();
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
        var setup = new EditorSetup { CanvasSettingsPath = settingsPath };
        var (_, bar) = WithBar(setup);

        bar.Loop.IsChecked.ShouldBe(false);
        bar.Loop.IsChecked = true;

        WithBar(setup).Bar.Loop.IsChecked.ShouldBe(true);
    }
}

/// <summary>The seek bar over the full-screen picture: tucked away at the top, and in step with the toolbar's.</summary>
public sealed class SeekOverlayTests : UiTest
{
    private static SeekOverlay Overlay(MainWindow window) => All<SeekOverlay>(window).Single();

    private static PreviewHost Preview(MainWindow window) => All<PreviewHost>(window).Single();

    private static void FullScreen(MainWindow window)
    {
        var preview = Preview(window);
        var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    /// <summary>Brings the pointer onto the dots, which is what opens them.</summary>
    private static void Reach(MainWindow window, SeekOverlay overlay)
    {
        var dots = overlay.Dots;
        var at = dots.TranslatePoint(new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2), window)!.Value;

        window.MouseMove(at);
        Settle(window);
    }

    [AvaloniaFact]
    public void The_seek_bar_is_over_the_picture_only_while_it_has_the_window()
    {
        var window = Open();

        Overlay(window).IsEffectivelyVisible.ShouldBeFalse();

        FullScreen(window);

        Overlay(window).IsEffectivelyVisible.ShouldBeTrue();
        Overlay(window).VerticalAlignment.ShouldBe(Avalonia.Layout.VerticalAlignment.Top);
        Overlay(window).IsOpen.ShouldBeFalse("it waits behind its dots");

        FullScreen(window);

        Overlay(window).IsEffectivelyVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Reaching_the_dots_opens_a_bar_that_moves_the_clock()
    {
        var window = Open();

        FullScreen(window);
        Reach(window, Overlay(window));

        Overlay(window).IsOpen.ShouldBeTrue();

        var track = Overlay(window).Track;
        var at = track.TranslatePoint(track.At(60), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Preview(window).Time.ShouldBe(60, 1);
    }

    [AvaloniaFact]
    public void Its_loop_switch_is_the_toolbars()
    {
        var window = Open();
        var loop = All<ToggleButton>(window).Single(b => b.Name == "seekLoop");

        FullScreen(window);
        Reach(window, Overlay(window));

        var button = All<Button>(Overlay(window)).Single();
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        loop.IsChecked.ShouldBe(true);
        Overlay(window).Looped.ShouldBeTrue();
    }
}
