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
/// The question a window with unsaved work asks on the way out, and the buttons
/// that answer it.
/// </summary>
/// <remarks>
/// These exist because of how the answer gets home. Each button has to take down
/// the dialog it is in and say what was chosen, and a button that does neither
/// compiles exactly like one that does — the result is a row of controls that
/// look right, highlight under the mouse, and do nothing whatever. No warning, no
/// exception. The only way to find out is to press one, so these press one.
/// <para>
/// The other half is that it is a panel over the window rather than a window of
/// its own, so nothing about being modal comes from the platform any more. What
/// used to be the operating system's promise — that the shell behind cannot be
/// clicked or typed at — is now three lines of this program's, and is tested
/// here as such.
/// </para>
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
    private static ModalOverlay Asking(MainWindow window)
    {
        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        return All<ModalOverlay>(window).SingleOrDefault()
            ?? throw new InvalidOperationException("closing an edited patch should have asked about it");
    }

    private static string[] Words(Visual root) =>
        All<TextBlock>(root).Select(t => t.Text ?? string.Empty).ToArray();

    /// <summary>Presses the button with this label, the way the mouse would.</summary>
    private static void Press(Visual dialog, string labelled)
    {
        var button = All<Button>(dialog).Single(b => b.Content as string == labelled);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Closing_an_edited_patch_asks_before_it_goes()
    {
        var window = OpenAndEdit();

        window.Close();

        var dialog = Asking(window);
        var labels = All<Button>(dialog).Select(b => b.Content as string).ToList();

        Words(dialog).ShouldContain("Unsaved changes");
        labels.ShouldContain("Save…");
        labels.ShouldContain("Discard changes");
        labels.ShouldContain("Cancel");
    }

    /// <summary>
    /// Every answer has to take the dialog down, or what was decided can never
    /// get back to the window and the only way out is the frame.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Discard changes")]
    [InlineData("Cancel")]
    public void Every_answer_closes_the_dialog(string labelled)
    {
        var window = OpenAndEdit();

        window.Close();

        Press(Asking(window), labelled);

        All<ModalOverlay>(window).ShouldBeEmpty($"'{labelled}' should have taken the dialog down");
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

    // --- the ways out that are not buttons -----------------------------------

    /// <summary>
    /// Dismissing is Cancel, and nothing in the method says so — a dialog taken
    /// down without a result comes back as the enum's default, and Cancel is
    /// declared first for exactly that reason. Worth a test because the
    /// guarantee lives in the order of an enum, where it is easy to disturb by
    /// accident.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("cross")]
    [InlineData("escape")]
    public void Dismissing_the_question_does_not_answer_it(string how)
    {
        var window = OpenAndEdit();

        window.Close();

        var dialog = Asking(window);

        if (how == "cross")
        {
            All<Button>(dialog).Single(b => b.Name == "dismiss")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        }

        Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeTrue("dismissing the question should not have answered it");
        All<NodeEditor>(window).Single().IsModified.ShouldBeTrue();
        All<ModalOverlay>(window).ShouldBeEmpty("and should have taken the question down");
    }

    /// <summary>
    /// Clicking beside the dialog is not one of the ways out. The sheet stops
    /// the click, and that is all it does: a question that a missed button press
    /// could dismiss is one that gets dismissed by accident.
    /// </summary>
    [AvaloniaFact]
    public void A_click_away_from_the_question_leaves_it_up()
    {
        var window = OpenAndEdit();

        window.Close();
        Asking(window);

        window.MouseDown(new Point(8, 8), MouseButton.Left);
        window.MouseUp(new Point(8, 8), MouseButton.Left);
        Settle(window);

        All<ModalOverlay>(window).ShouldHaveSingleItem("the question should still be up");
    }

    // --- and that it is actually modal ---------------------------------------

    /// <summary>The sheet is over all of the window, or there is a way round it.</summary>
    [AvaloniaFact]
    public void The_sheet_covers_the_whole_window()
    {
        var window = OpenAndEdit();

        window.Close();

        var dialog = Asking(window);

        dialog.Bounds.Width.ShouldBe(window.ClientSize.Width, 1);
        dialog.Bounds.Height.ShouldBe(window.ClientSize.Height, 1);
    }

    /// <summary>
    /// The window listens for Ctrl+Z above whatever has the focus, so without
    /// the overlay swallowing keys it would undo the very edit it is asking
    /// about while the question was still on screen.
    /// </summary>
    [AvaloniaFact]
    public void The_shell_does_not_hear_the_keyboard_while_the_question_is_up()
    {
        var window = OpenAndEdit();
        var editor = All<NodeEditor>(window).Single();
        var nodes = editor.Patch.Nodes.Count;

        window.Close();
        Asking(window);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        editor.Patch.Nodes.Count.ShouldBe(nodes, "Ctrl+Z should not have reached the canvas");
    }

    /// <summary>
    /// And the mouse stops at the sheet rather than reaching the patch. Both
    /// halves are here: a click on bare canvas clears the selection, so the test
    /// that it does not is only worth anything beside the one that it otherwise
    /// would.
    /// </summary>
    [AvaloniaFact]
    public void The_shell_cannot_be_clicked_while_the_question_is_up()
    {
        var window = OpenAndEdit();
        var editor = All<NodeEditor>(window).Single();

        var selected = editor.SelectedNode.ShouldNotBeNull("adding a module should have selected it").Id;

        // A corner of the canvas that is on screen and has nothing on it.
        var empty = editor.TranslatePoint(new Point(24, editor.Bounds.Height - 24), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.Close();

        var dialog = Asking(window);

        Click(window, empty);
        editor.SelectedNode?.Id.ShouldBe(selected, "the click should have stopped at the sheet");

        Press(dialog, "Cancel");

        Click(window, empty);
        editor.SelectedNode.ShouldBeNull("and reached the canvas once the question was gone");

        void Click(Window on, Point at)
        {
            on.MouseDown(at, MouseButton.Left);
            on.MouseUp(at, MouseButton.Left);
            Settle(on);
        }
    }
}
