using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The picture going full screen on a monitor of its own while the editor stays
/// where it is.
/// </summary>
/// <remarks>
/// Headless has one screen, so the window is sent to that one directly: which
/// monitor is picked is <see cref="Flyback.App.Tests.MonitorPickTests"/>'s business.
/// </remarks>
public class FullScreenElsewhereTests : UiTest
{

    private static PreviewHost Preview(MainWindow window) =>
        window.OwnedWindows.SelectMany(All<PreviewHost>).SingleOrDefault()
        ?? All<PreviewHost>(window).Single();

    private static void SendAway(MainWindow window)
    {
        window.ShowPictureOn(window.Screens.ScreenFromWindow(window).ShouldNotBeNull());
        Settle(window);
    }

    private static void PressEscape(MainWindow window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    [AvaloniaFact]
    public void The_picture_goes_to_a_window_of_its_own_and_the_editor_stays()
    {
        var window = Open();

        SendAway(window);

        var picture = window.OwnedWindows.ShouldHaveSingleItem();

        picture.WindowState.ShouldBe(WindowState.FullScreen);
        TopLevel.GetTopLevel(Preview(window)).ShouldBeSameAs(picture);

        All<NodeEditor>(window).Single().IsEffectivelyVisible.ShouldBeTrue("the editor is still to be worked in");
        All<TextBlock>(window).Single(t => t.Name == "pictureAway").IsEffectivelyVisible
            .ShouldBeTrue("where the picture was says where it went");
    }

    [AvaloniaFact]
    public void Escape_in_the_editor_brings_the_picture_back()
    {
        var window = Open();
        var preview = Preview(window);
        var parent = preview.GetVisualParent();

        SendAway(window);
        PressEscape(window);

        window.OwnedWindows.ShouldBeEmpty();
        preview.GetVisualParent().ShouldBeSameAs(parent, "back in the box it left");
        All<TextBlock>(window).ShouldNotContain(t => t.Name == "pictureAway");
    }

    [AvaloniaFact]
    public void Closing_the_picture_window_brings_the_picture_back()
    {
        var window = Open();
        var preview = Preview(window);

        SendAway(window);
        window.OwnedWindows.Single().Close();
        Settle(window);

        TopLevel.GetTopLevel(preview).ShouldBeSameAs(window);
    }

    /// <summary>A renderer stops for good when it leaves the tree, so each move builds a fresh one.</summary>
    [AvaloniaFact]
    public void The_picture_keeps_drawing_after_each_move()
    {
        var window = Open();
        var preview = Preview(window);
        var before = preview.Child;

        SendAway(window);

        var away = preview.Child;
        away.ShouldNotBeSameAs(before);

        PressEscape(window);

        preview.Child.ShouldNotBeSameAs(away);
        preview.Child.ShouldNotBeNull().IsEffectivelyVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_picture_on_another_monitor_has_its_own_transport_in_step_with_the_editor()
    {
        var window = Open();

        SendAway(window);

        var picture = window.OwnedWindows.ShouldHaveSingleItem();
        var transport = All<TransportOverlay>(picture).ShouldHaveSingleItem();
        var paused = transport.Paused;

        transport.IsEffectivelyVisible.ShouldBeTrue();

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Settle(window);

        transport.Paused.ShouldBe(!paused, "pausing in the editor shows on the picture's transport");
    }

    [AvaloniaFact]
    public void The_picture_on_another_monitor_has_a_seek_bar_that_follows_the_clock()
    {
        SeekBar? bar = null;
        var window = Open(replace: services => services.AddSingleton(sp => bar = ActivatorUtilities.CreateInstance<SeekBar>(sp)));

        SendAway(window);

        var picture = window.OwnedWindows.ShouldHaveSingleItem();
        var seek = All<SeekOverlay>(picture).ShouldHaveSingleItem();

        seek.IsEffectivelyVisible.ShouldBeTrue();

        All<PreviewHost>(picture).Single().Time = 42;
        bar.ShouldNotBeNull().Update();

        seek.Track.Value.ShouldBe(42, 1);
    }
}
