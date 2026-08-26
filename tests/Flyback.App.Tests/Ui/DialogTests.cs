using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The two dialogs that ask nothing — Settings and About — opened from the
/// toolbar and dismissed again.
/// </summary>
/// <remarks>
/// Opening each of them twice is the point. Neither has a button of its own, so
/// the only way out is the frame the overlay draws; and the settings panel is
/// lent to the dialog rather than built for it, so what it was last shown in has
/// to give it back. A control has one parent, and the failure when it is not
/// returned is a throw on the second opening — a bug nobody meets until the
/// second time they look at a setting.
/// </remarks>
public class DialogTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);

        return window;
    }

    /// <summary>Presses a toolbar button and waits for what it puts up.</summary>
    private static ModalOverlay Show(MainWindow window, string named)
    {
        All<Button>(window).Single(b => b.Name == named)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        return All<ModalOverlay>(window).SingleOrDefault()
            ?? throw new InvalidOperationException($"'{named}' should have opened a dialog");
    }

    private static void Dismiss(MainWindow window, ModalOverlay dialog)
    {
        All<Button>(dialog).Single(b => b.Name == "dismiss")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Dispatcher.UIThread.RunJobs();
        Settle(window);
    }

    [AvaloniaTheory]
    [InlineData("settings", "Settings")]
    [InlineData("about", "About")]
    public void It_opens_says_what_it_is_and_can_be_dismissed(string named, string titled)
    {
        var window = Open();
        var dialog = Show(window, named);

        All<TextBlock>(dialog).Select(t => t.Text).ShouldContain(titled);

        Dismiss(window, dialog);

        All<ModalOverlay>(window).ShouldBeEmpty("the cross should have taken it down");
    }

    /// <summary>
    /// And again. The settings panel is the shell's, not the dialog's — it holds
    /// what was last typed into it — so the dialog it was shown in has to let go
    /// of it on the way out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("settings")]
    [InlineData("about")]
    public void It_opens_a_second_time(string named)
    {
        var window = Open();

        Dismiss(window, Show(window, named));

        var again = Show(window, named);

        All<TextBlock>(again).ShouldNotBeEmpty("the second opening should have contents");
    }

    /// <summary>
    /// Two dialogs are never up at once, and not because anything counts them:
    /// the button that would open the second is under the sheet, and the sheet
    /// takes the click and does nothing with it.
    /// </summary>
    [AvaloniaFact]
    public void A_click_over_the_toolbar_reaches_the_sheet_and_not_the_button()
    {
        var window = Open();

        var about = All<Button>(window).Single(b => b.Name == "about");
        var at = about.TranslatePoint(about.Bounds.Center - about.Bounds.Position, window)
            ?? throw new InvalidOperationException("the toolbar is not in this window");

        Show(window, "settings");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        var still = All<ModalOverlay>(window).ShouldHaveSingleItem();

        All<TextBlock>(still).Select(t => t.Text)
            .ShouldContain("Settings", "the About button under the sheet should not have been pressed");
    }
}
