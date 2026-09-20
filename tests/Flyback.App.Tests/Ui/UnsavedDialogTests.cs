using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;
using Xunit;
using System.Threading;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The question a window with unsaved work asks on the way out, and the buttons
/// that answer it.
/// </summary>
/// <remarks>
/// Each button has to take down the dialog and say what was chosen, and one that
/// does neither compiles exactly like one that does — a row of controls that look
/// right, highlight under the mouse, and do nothing. The only way to find out is to
/// press one. The other half is that it is a panel rather than a window, so nothing
/// about being modal comes from the platform.
/// </remarks>
public class UnsavedDialogTests : UiTest
{
    /// <summary>Where the tests that write a file write it.</summary>
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-unsaved-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>A window whose patch has been edited, so closing it has to ask.</summary>
    private MainWindow OpenAndEdit()
    {
        var window = NewMainWindow();

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

    /// <summary>
    /// Typing that was never applied is asked about too.
    /// </summary>
    /// <remarks>
    /// It is the one thing the canvas's history cannot know about: nothing typed
    /// reaches the patch until somebody asks for it, so a window holding an
    /// afternoon of writing over a patch nobody touched used to close without a
    /// word. Picking a preset in the same state has always asked, which is what
    /// made the silence on the way out a bug rather than a policy.
    /// </remarks>
    [AvaloniaFact]
    public void Closing_over_typing_that_was_never_applied_asks_as_well()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        // Read into text, which is what picking a preset from the text view
        // does: the text is the document from here, with nothing in it to lose.
        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        Pick(All<ComboBox>(window).First(box => box.Name == "presets"), "Kaleidoscope");
        Settle(window);

        All<NodeEditor>(window).Single().IsModified
            .ShouldBeFalse("the patch itself is untouched, which is the whole point");

        // Through the document, which is what typing is.
        All<AvaloniaEdit.TextEditor>(window).Single(b => b.Name == "source")
            .Document.Insert(0, "# a note to myself\n");

        Settle(window);

        window.Close();

        Words(Asking(window)).ShouldContain("Unsaved changes");
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

    /// <summary>
    /// Closing again while the question is up is ignored. The dialog is a panel
    /// over the window rather than a window of its own, so the frame's cross is
    /// still there to be clicked a second time — and a second question stacked
    /// on the first would have to be answered twice to get out of one close.
    /// </summary>
    [AvaloniaFact]
    public void Closing_again_while_the_question_is_up_does_not_ask_twice()
    {
        var window = OpenAndEdit();

        window.Close();
        Asking(window);

        window.Close();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        All<ModalOverlay>(window).ShouldHaveSingleItem("the second close should have been ignored");

        // And the one question still answers for the whole thing: cancelling
        // leaves the window up rather than leaving a second close pending.
        Press(All<ModalOverlay>(window).Single(), "Cancel");

        for (var attempt = 0; attempt < 20 && window.IsVisible; attempt++)
            Dispatcher.UIThread.RunJobs();

        window.IsVisible.ShouldBeTrue("cancelling should have kept the window");
        All<ModalOverlay>(window).ShouldBeEmpty();
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

    // --- saving a text document as something else ------------------------------

    /// <summary>The platform's own file-backed storage file — see <c>FileDropTests</c>.</summary>
    private static Avalonia.Platform.Storage.IStorageFile RealStorageFile(string path)
    {
        var type = typeof(Avalonia.Platform.Storage.IStorageFile).Assembly.GetType(
            "Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;

        var flags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance;

        return (Avalonia.Platform.Storage.IStorageFile)type.GetConstructors(flags)[0].Invoke([new FileInfo(path)]);
    }

    /// <summary>A window whose document is text that has been applied and written nowhere.</summary>
    private MainWindow OpenOnUnsavedText()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        All<AvaloniaEdit.TextEditor>(window).Single(b => b.Name == "source").Text = """
            # a tone, and a note about it
            let hum = t |> sine(freq: 220)
            hum |> out.left
            """;

        All<Button>(window).Single(b => b.Name == "apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        All<NodeEditor>(window).Single().Locked.ShouldBeTrue("the text is the document");

        return window;
    }

    private static bool Finished(Task<bool> saving)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!saving.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        saving.IsCompleted.ShouldBeTrue("the save should have finished");

        return saving.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Saving a text document as a patch hands it to the graph and empties the text
    /// (ADR-0068), so text that is written nowhere else is asked about first — and
    /// backing out writes nothing.
    /// </summary>
    [AvaloniaFact]
    public void Saving_unsaved_text_as_a_patch_asks_first_and_cancel_writes_nothing()
    {
        var window = OpenOnUnsavedText();
        var path = Path.Combine(folder, "hum.fbk");

        Directory.CreateDirectory(folder);

        var saving = window.SaveToAsync(RealStorageFile(path));
        var dialog = Asking(window);

        Words(dialog).ShouldContain("Unsaved text");
        All<Button>(dialog).Select(b => b.Content as string).ShouldNotContain("Save…", "saving is what asked");

        Press(dialog, "Cancel");

        Finished(saving).ShouldBeFalse("nothing was saved");
        File.Exists(path).ShouldBeFalse();

        All<NodeEditor>(window).Single().Locked.ShouldBeTrue("and the text is still the document");
        All<AvaloniaEdit.TextEditor>(window).Single(b => b.Name == "source").Text.ShouldContain("a note about it");
    }

    /// <summary>And going ahead writes the patch and hands it to the canvas.</summary>
    [AvaloniaFact]
    public void Saving_unsaved_text_as_a_patch_goes_ahead_when_told_to()
    {
        var window = OpenOnUnsavedText();
        var path = Path.Combine(folder, "hum.fbk");

        Directory.CreateDirectory(folder);

        var saving = window.SaveToAsync(RealStorageFile(path));

        Press(Asking(window), "Save without the text");

        Finished(saving).ShouldBeTrue();
        File.Exists(path).ShouldBeTrue();

        All<NodeEditor>(window).Single().Locked.ShouldBeFalse("a patch file is the document, so the graph owns it");
    }

    /// <summary>Saved as text there is nothing to lose, and nothing is asked.</summary>
    [AvaloniaFact]
    public void Saving_unsaved_text_as_text_asks_nothing()
    {
        var window = OpenOnUnsavedText();
        var path = Path.Combine(folder, "hum.fbks");

        Directory.CreateDirectory(folder);

        Finished(window.SaveToAsync(RealStorageFile(path))).ShouldBeTrue();

        All<ModalOverlay>(window).ShouldBeEmpty();
        File.ReadAllText(path).ShouldContain("a note about it");
    }

    // --- and saving as text ------------------------------------------------------

    /// <summary>
    /// A printing written from the unsaved-changes question does not count as
    /// the save.
    /// </summary>
    /// <remarks>
    /// A graph-owned patch saved as text is a copy that drops the groups
    /// (ADR-0068) and leaves the patch unsaved, and the status line says so. The
    /// save answers false for the same reason: "Save…" in the question goes on
    /// to close the window or replace the patch only on a true, and that would
    /// be the only whole copy gone.
    /// </remarks>
    [AvaloniaFact]
    public void A_printing_written_from_the_question_is_not_taken_for_the_save()
    {
        var window = OpenAndEdit();
        var editor = All<NodeEditor>(window).Single();

        Directory.CreateDirectory(folder);

        var went = Finished(window.SaveToAsync(RealStorageFile(Path.Combine(folder, "copy.fbks"))));
        Settle(window);

        editor.IsModified.ShouldBeTrue("a printing is a copy, and the patch is as unsaved as it was");

        went.ShouldBeFalse("so the question that asked for a save has not had one, and must not go ahead");
    }

    /// <summary>
    /// A bundle saved as text puts what it was carrying beside the text, the way
    /// saving it as a patch file does (ADR-0060).
    /// </summary>
    /// <remarks>
    /// The saved text names <c>files/kick.wav</c>, which until then is nowhere
    /// but in the memory of a window that no longer says it is a bundle. Written
    /// beside the text, it still plays the next time the text is opened.
    /// </remarks>
    [AvaloniaFact]
    public void A_bundle_saved_as_text_puts_its_files_beside_the_text()
    {
        const string carriedPath = "files/kick.wav";

        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var player = b.Add(NodeCatalog.SampleTypeId, 200, 40);
        SampleExtra.Set(player, carriedPath);
        b.Wire(player, 0, output, NodeCatalog.OutputLeftPort);

        var window = NewMainWindow();

        window.Show();
        Settle(window);

        window.Became("nebula", beside: null, new BundleFiles(
            new Dictionary<string, byte[]> { [carriedPath] = [1, 2, 3, 4] }));
        All<NodeEditor>(window).Single().Patch = b.Patch;
        Settle(window);

        window.IsBundle.ShouldBeTrue();

        // The text takes the patch, so text is what Save offers first.
        All<ToggleButton>(window).Single(t => t.Name == "code").IsChecked = true;
        Settle(window);

        All<Button>(window).Single(a => a.Name == "apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        All<NodeEditor>(window).Single().Locked.ShouldBeTrue("the text is the document");

        Directory.CreateDirectory(folder);

        var saved = Path.Combine(folder, "nebula.fbks");

        Finished(window.SaveToAsync(RealStorageFile(saved))).ShouldBeTrue();
        Settle(window);

        File.ReadAllText(saved).ShouldContain("kick.wav", customMessage: "the text names the file");

        File.Exists(Path.Combine(folder, "files", "kick.wav"))
            .ShouldBeTrue("and the file it names is where it says");
    }
}
