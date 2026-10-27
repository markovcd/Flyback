using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Shell;
using Flyback.Editor.Windows;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Windows;

/// <summary>
/// Live patching with no keyboard: the transport row's Live button goes there, the transport
/// over the picture swaps the patch and its text and gives the window back, and a
/// phone's Back leaves too.
/// </summary>
public class LiveByTouchTests : EditorTest
{
    private static void Click(MainWindow window, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    private static Button Tool(MainWindow window, string name) => All<Button>(window).Single(b => b.Name == name);

    private void GoLive(MainWindow window) => Click(window, Service<TransportRow>(window).Live);

    [AvaloniaFact]
    public void The_Live_button_puts_the_modules_over_the_full_screen_picture()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        GoLive(window);

        live.IsOn.ShouldBeTrue();
        live.Coding.ShouldBeFalse();
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeTrue();
        Tool(window, "liveLeave").IsVisible.ShouldBeTrue("the transport over the picture has the way out");
        Service<TransportControls>(window).Overlay.ShouldNotBeNull().PatchOver.ShouldBeTrue();
        var dots = Service<TransportControls>(window).Overlay!;
        window.MouseMove(dots.TranslatePoint(new Point(dots.Bounds.Width / 2, 18), window)!.Value);
        Settle(window);
        Shoot(window, "live-by-touch.png");
    }

    [AvaloniaFact]
    public void The_text_button_swaps_the_modules_for_the_text_and_back()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        GoLive(window);
        Click(window, Tool(window, "liveText"));

        live.Coding.ShouldBeTrue();
        Service<TransportControls>(window).Overlay!.Coding.ShouldBeTrue();

        Click(window, Tool(window, "liveText"));

        live.IsOn.ShouldBeTrue();
        live.Coding.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_patch_button_takes_the_patch_off_the_picture_and_puts_it_back()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        GoLive(window);
        Click(window, Tool(window, "livePatch"));

        live.IsOn.ShouldBeFalse();
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeTrue("the picture keeps the window");

        Click(window, Tool(window, "livePatch"));

        live.IsOn.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_leave_button_gives_the_window_back()
    {
        var window = Open();

        GoLive(window);
        Click(window, Tool(window, "liveLeave"));

        Service<LivePatching>(window).IsOn.ShouldBeFalse();
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Back_leaves_the_full_screen_picture()
    {
        var window = Open();

        GoLive(window);

        var back = new RoutedEventArgs(TopLevel.BackRequestedEvent);
        window.RaiseEvent(back);
        Settle(window);

        back.Handled.ShouldBeTrue("the app is not closed under the picture");
        Service<FullScreenPreview>(window).IsFullScreen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void In_a_page_live_patching_asks_for_the_whole_screen_and_gives_it_back()
    {
        var asked = new List<bool>();
        var window = NewMainWindow(new EditorSetup { Host = new() { InPage = true, WholeScreen = asked.Add } });
        window.Show();
        Settle(window);

        GoLive(window);
        Click(window, Tool(window, "liveLeave"));

        asked.ShouldBe([true, false]);
    }

    /// <summary>A page's picture is the browser's, drawn above the editor, so it goes under it and nothing between hides it.</summary>
    [AvaloniaFact]
    public void A_picture_the_host_draws_goes_behind_the_editor_with_nothing_opaque_over_it()
    {
        var moved = new List<bool>();
        var window = NewMainWindow(new EditorSetup { Host = new() { InPage = true, PictureBehind = moved.Add } });
        window.Show();
        Settle(window);

        var box = Service<FullScreenPreview>(window).PreviewBox!;

        GoLive(window);

        moved.ShouldBe([true]);
        for (Avalonia.Visual? at = box; at is not null; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
        {
            var ground = at switch { Panel p => p.Background, Border b => b.Background, Avalonia.Controls.Primitives.TemplatedControl t => t.Background, _ => null };
            (ground is null || ground == Avalonia.Media.Brushes.Transparent).ShouldBeTrue($"{at.GetType().Name} hides the picture");
        }

        Click(window, Tool(window, "liveLeave"));

        moved.ShouldBe([true, false]);
        box.Background.ShouldBe(Avalonia.Media.Brushes.Black, "the picture's box has its ground back");
    }

    [AvaloniaFact]
    public void Text_over_the_picture_says_it_wants_a_keyboard_until_a_key_is_pressed()
    {
        var window = Open();
        var live = Service<LivePatching>(window);

        GoLive(window);
        Click(window, Tool(window, "liveText"));

        live.KeyboardHinted.ShouldBeTrue();

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Settle(window);

        live.KeyboardHinted.ShouldBeFalse();

        Click(window, Tool(window, "liveText"));
        Click(window, Tool(window, "liveText"));

        live.KeyboardHinted.ShouldBeFalse("once a keyboard has been used, it is not asked for again");
    }
}
