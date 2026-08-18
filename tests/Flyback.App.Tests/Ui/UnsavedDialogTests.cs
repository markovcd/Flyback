using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The question a window with unsaved work asks on the way out, and the buttons
/// that answer it.
/// </summary>
/// <remarks>
/// These exist because of how the answer gets home. Each button has to close the
/// window it is in and say what was chosen, and a button that does neither
/// compiles exactly like one that does — the result is a row of controls that
/// look right, highlight under the mouse, and do nothing whatever. No warning, no
/// exception. The only way to find out is to press one, so these press one.
/// </remarks>
public class UnsavedDialogTests : UiTest
{
    /// <summary>A window whose patch has been edited, so closing it has to ask.</summary>
    private static MainWindow OpenAndEdit()
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);

        // A real edit rather than a notification of one: the history compares
        // snapshots and records nothing for a patch that has not changed, so
        // announcing a change is not enough to make there be one.
        var editor = All<NodeEditor>(window).Single();
        editor.AddNode("value").ShouldNotBeNull();

        editor.IsModified.ShouldBeTrue("the window should have something to ask about");

        return window;
    }

    /// <summary>Pumps until the dialog the closing window puts up has arrived.</summary>
    private static Window Asking(MainWindow window)
    {
        for (var attempt = 0; attempt < 20 && window.OwnedWindows.Count == 0; attempt++)
            Dispatcher.UIThread.RunJobs();

        return window.OwnedWindows.SingleOrDefault()
            ?? throw new InvalidOperationException("closing an edited patch should have asked about it");
    }

    /// <summary>Presses the button with this label, the way the mouse would.</summary>
    private static void Press(Window dialog, string labelled)
    {
        var button = All<Button>(dialog).Single(b => b.Content as string == labelled);

        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Closing_an_edited_patch_asks_before_it_goes()
    {
        var window = OpenAndEdit();

        window.Close();

        var dialog = Asking(window);
        var labels = All<Button>(dialog).Select(b => b.Content as string).ToList();

        dialog.Title.ShouldBe("Unsaved changes");
        labels.ShouldContain("Save…");
        labels.ShouldContain("Discard changes");
        labels.ShouldContain("Cancel");
    }

    /// <summary>
    /// Every answer has to shut the dialog, or what was decided can never get
    /// back to the window and the only way out is the frame.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Discard changes")]
    [InlineData("Cancel")]
    public void Every_answer_closes_the_dialog(string labelled)
    {
        var window = OpenAndEdit();

        window.Close();

        var dialog = Asking(window);
        Press(dialog, labelled);

        dialog.IsVisible.ShouldBeFalse($"'{labelled}' should have closed the dialog");
        window.OwnedWindows.ShouldBeEmpty("and left nothing behind it");
    }

    /// <summary>
    /// Discarding lets the window go — the half of the answer that only means
    /// anything once the dialog has actually closed and said which button it was.
    /// </summary>
    [AvaloniaFact]
    public void Discarding_lets_the_window_close()
    {
        var window = OpenAndEdit();

        window.Close();
        Press(Asking(window), "Discard changes");

        for (var attempt = 0; attempt < 20 && window.IsVisible; attempt++)
            Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeFalse("discarding should have let the close go through");
    }

    /// <summary>
    /// Cancelling keeps the window and the work in it.
    /// </summary>
    [AvaloniaFact]
    public void Cancelling_keeps_the_window_and_the_edit()
    {
        var window = OpenAndEdit();

        window.Close();
        Press(Asking(window), "Cancel");

        window.IsVisible.ShouldBeTrue("cancelling should have kept the window");
        All<NodeEditor>(window).Single().IsModified.ShouldBeTrue("and the work in it");
    }

    /// <summary>
    /// The frame is Cancel, and nothing in the method says so — a dialog closed
    /// without a result comes back as the enum's default, and Cancel is declared
    /// first for exactly that reason. Worth a test because the guarantee lives in
    /// the order of an enum, where it is easy to disturb by accident.
    /// </summary>
    [AvaloniaFact]
    public void Closing_by_the_frame_loses_nothing()
    {
        var window = OpenAndEdit();

        window.Close();

        Asking(window).Close();
        Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeTrue("dismissing the question should not have answered it");
        All<NodeEditor>(window).Single().IsModified.ShouldBeTrue();
    }
}
