using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The seek bar on the toolbar: dragging it moves the patch's clock, paused or not,
/// and its length is what was typed beside it, kept for the next window.
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

    private MainWindow OpenWithSettings() => Open(setup: new EditorSetup { CanvasSettingsPath = settingsPath });

    private static Slider Track(MainWindow window) => All<Slider>(window).Single(s => s.Name == "seek");

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

    [AvaloniaFact]
    public void Moving_the_bar_moves_the_clock()
    {
        var window = Open();

        Track(window).Value = 30;

        Preview(window).Time.ShouldBe(30, 0.5);
    }

    [AvaloniaFact]
    public void A_paused_patch_moved_along_the_bar_stays_paused_there()
    {
        var window = Open();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        Track(window).Value = 45;
        Settle(window);

        Preview(window).Time.ShouldBe(45);
        Preview(window).Clock.ShouldNotBeNull().Invoke().ShouldBe(45);
    }

    [AvaloniaFact]
    public void A_clock_past_the_end_holds_the_thumb_there()
    {
        SeekBar? bar = null;
        var window = Open(replace: services => services.AddSingleton(sp => bar = ActivatorUtilities.CreateInstance<SeekBar>(sp)));

        Preview(window).Time = 500;
        bar.ShouldNotBeNull().Update();

        Track(window).Value.ShouldBe(Track(window).Maximum);
    }

    [AvaloniaFact]
    public void The_bar_spans_the_length_typed_and_the_next_window_opens_with_it()
    {
        var window = OpenWithSettings();

        Track(window).Maximum.ShouldBe(60);

        TypeLength(window, "2:30");

        Track(window).Maximum.ShouldBe(150);
        Length(window).Text.ShouldBe("2:30");
        Track(Open(setup: new EditorSetup { CanvasSettingsPath = settingsPath })).Maximum.ShouldBe(150);
    }

    [AvaloniaFact]
    public void A_length_that_cannot_be_read_puts_the_old_one_back()
    {
        var window = Open();

        TypeLength(window, "soon");

        Track(window).Maximum.ShouldBe(60);
        Length(window).Text.ShouldBe("1:00");
    }
}

/// <summary>What the seek bar's length box reads and shows.</summary>
public sealed class SeekLengthTests
{
    [Theory]
    [InlineData("90", 90d)]
    [InlineData("1:30", 90d)]
    [InlineData(" 2:05 ", 125d)]
    [InlineData("1:30.4", 90d)]
    [InlineData("10:00", 600d)]
    public void A_length_is_read_as_seconds_or_minutes_and_seconds(string typed, double seconds) =>
        SeekBar.Parse(typed).ShouldBe(seconds);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1:2:3")]
    [InlineData("100000")]
    public void A_length_it_cannot_span_is_turned_away(string typed) => SeekBar.Parse(typed).ShouldBeNull();

    [Fact]
    public void A_length_is_shown_as_minutes_and_seconds() => SeekBar.Say(90).ShouldBe("1:30");
}
