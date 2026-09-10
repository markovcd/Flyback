using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// The editing session as opposed to the patch: undo and redo from wherever the
/// focus is, what the title bar says about unsaved work, and the question every
/// route out of a patch has to ask first.
/// </summary>
/// <remarks>
/// The canvas owns the history and answers whether there is anything to lose;
/// what is here is the asking. One method fronts every way a patch can be closed,
/// so none of those callers has to know whether anything was edited.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>The window title, before anything is said about the patch in it.</summary>
    private const string BaseTitle = GlobalConstants.ApplicationName;

    /// <summary>
    /// What the patch on the canvas is called, or null for one with no name of its
    /// own yet.
    /// </summary>
    /// <remarks>
    /// The file it was opened from or last written to, or the preset it was built
    /// from. Kept rather than worked out, because after a Save As there is no other
    /// record of which file on the disk is the one on screen. Without the
    /// extension, because a preset has none — and a picker adds one itself.
    /// </remarks>
    private string? patchName;

    /// <summary>
    /// Whether this document is a bundle. What it decides is small and worth
    /// having: which kind the save dialog offers first, so that a bundle saved
    /// again stays one without anybody typing an extension.
    /// </summary>
    private bool bundled;

    /// <summary>
    /// Set once the question about unsaved work has been asked and answered, so
    /// the second Close does not ask it again. A close has to be cancelled to
    /// put a dialog up at all — nothing may block inside OnClosing — so the way
    /// back out is to close again once there is an answer.
    /// </summary>
    private bool leaving;

    /// <summary>
    /// Set while the question is on the screen and being answered. The dialog is
    /// a panel over this window rather than a window of its own, so the frame's
    /// cross stays live underneath it; a second close arriving while the first
    /// is still being dealt with is ignored rather than allowed to stack a
    /// second copy of the same question.
    /// </summary>
    private bool questionIsUp;

    /// <summary>What to do about a patch that has been edited and not written out.</summary>
    private enum Unsaved
    {
        /// <summary>Refused, and whatever asked should not go ahead.</summary>
        Cancel,

        Save,

        Discard,
    }

    /// <summary>
    /// Whether the thing about to replace or close the patch may go ahead. Asks
    /// only when there is something to lose, so every caller can front its own
    /// action with this and none of them has to know whether anything was
    /// edited.
    /// </summary>
    private async Task<bool> MayReplaceThePatchAsync()
    {
        if (!SomethingToLose) return true;

        return await AnsweredAsync(
            "Unsaved changes",
            "This patch has changes that have not been saved. Closing it now would lose them.");
    }

    /// <summary>
    /// Whether this document has anything in it that closing would lose.
    /// </summary>
    /// <remarks>
    /// Two halves, because there are two places work can be: typing that has not
    /// been applied is the one the editor's history cannot know about. Asked by the
    /// question, by the close that puts it up and by the dot in the title, so the
    /// three cannot come to disagree.
    /// </remarks>
    private bool SomethingToLose => editor.IsModified || SourceIsUnapplied;

    /// <summary>
    /// Whether text about to stop being the document may go. Asks only about typing
    /// that is nowhere else: text already written out as <c>.fbks</c> is on disk.
    /// </summary>
    /// <remarks>
    /// The patch is deliberately not asked about, because it is not going anywhere:
    /// handing it back to the canvas changes who owns it and not what it is, so the
    /// question a file asks is still there to be asked.
    /// </remarks>
    private async Task<bool> MayLoseTheTextAsync()
    {
        if (!SourceIsUnapplied) return true;

        return await AnsweredAsync(
            "Unsaved text",
            "This text has not been saved. Handing the patch back to the canvas empties it, "
            + "and its comments, its names and its defs go with it — the patch itself is "
            + "untouched.");
    }

    /// <summary>
    /// Puts the three answers up and does what the answer says, for whoever is
    /// about to lose something.
    /// </summary>
    private async Task<bool> AnsweredAsync(string about, string question)
    {
        // A question is already up. Whatever asked is refused rather than queued
        // behind the first answer: it is the same document and the same three
        // buttons, and one set of them is already on the screen.
        if (questionIsUp) return false;

        questionIsUp = true;

        try
        {
            return await AskAboutUnsavedAsync(about, question) switch
            {
                // A cancelled save picker is a cancelled close: somebody who asked
                // to save and then thought better of where has not agreed to lose
                // the patch, and the safe reading of that is to stay put.
                Unsaved.Save => await SavePatchAsync(),
                Unsaved.Discard => true,
                _ => false,
            };
        }
        finally
        {
            questionIsUp = false;
        }
    }

    /// <summary>
    /// The three answers, as a window rather than as a system message box — there
    /// is no such thing here, and one built by hand is the same three buttons in
    /// the same palette as the rest of the shell.
    /// </summary>
    /// <remarks>
    /// Closing it by its own frame is Cancel, which is why Cancel is the enum's
    /// default too: a dialog closed without setting a result comes back as
    /// <c>default</c>, so the answer nobody gave is harmless by the language's own
    /// rule.
    /// </remarks>
    private async Task<Unsaved> AskAboutUnsavedAsync(string about, string question)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(Answering("Save…", Unsaved.Save));
        buttons.Children.Add(Answering("Discard changes", Unsaved.Discard, wide: true));
        buttons.Children.Add(Answering("Cancel", Unsaved.Cancel));

        var asking = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            MaxWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = question,
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        return await this.ShowDialog<Unsaved>(about, asking);

        static Button Answering(string text, Unsaved with, bool wide = false)
        {
            var button = new Button { Content = text, MinWidth = wide ? 120 : 96 };
            button.Click += (_, _) => Dialog.Close(button, with);

            return button;
        }
    }

    /// <summary>
    /// Nothing may block inside a closing handler, so a window with unsaved work
    /// in it cancels the close, asks, and closes itself again on the way back.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || leaving) return;

        // Already asking. The close is refused and nothing else happens: putting
        // the question up a second time is the one response that would make the
        // window look broken, and there is nothing else to do with a close that
        // arrived while the same close is still being answered.
        if (questionIsUp)
        {
            e.Cancel = true;
            return;
        }

        if (!SomethingToLose) return;

        e.Cancel = true;

        if (!await MayReplaceThePatchAsync()) return;

        leaving = true;
        Close();
    }

    /// <summary>

    /// Undo and redo, from wherever the focus happens to be. Handled on the window
    /// rather than on the canvas because an edit is as likely to have been made in
    /// the inspector, and anything that already dealt with the key keeps it — a
    /// text box undoing its own typing is doing the same job at its own scale.
    /// </summary>
    /// <remarks>
    /// Command as well as Control, so the shortcut is the one the machine uses.
    /// Both are accepted everywhere rather than asked which platform this is, since
    /// neither is a gesture anything else here claims.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled) return;

        // Before the modifier check, because Escape carries none. Only while the
        // preview has the window: everywhere else Escape belongs to the module
        // filter, which handles its own before this is ever reached.
        if (e.Key == Key.Escape && previewIsFullScreen)
        {
            ShowFullScreenPreview(false);
            e.Handled = true;
            return;
        }

        // Before the instrument, because F2 is not a note and never will be:
        // the keyboard-as-instrument maps letters, and a function key is free
        // for the shell in a way no letter is any more.
        if (e.Key == Key.F2)
        {
            ShowCode(!showingCode);
            e.Handled = true;
            return;
        }

        // The computer's keyboard as an instrument. A note is a bare keystroke
        // and nothing else, so a key carrying a command modifier is left for
        // whatever claimed it: Ctrl+Z is undo, and it stays undo in a patch
        // being played with a hand on the Z. Only while something is actually
        // listening, so a patch with no MIDI In in it types the way it always
        // did.
        if (Bare(e.KeyModifiers) && Playing && PlayKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        var again = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        switch (e.Key)
        {
            // All three go to whichever view is showing. Reached only where the
            // view did not want the keystroke itself: the code editor handles
            // its own undo, and this is the end of the bubble.
            case Key.Z:
                if (again) Redo();
                else Undo();
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                Redo();
                e.Handled = true;
                break;

            // Lay out. Beside the two above because it is the same kind of
            // thing: an edit that Ctrl+Z takes off again — the modules across
            // the canvas, or the lines down the page.
            case Key.L:
                Tidy();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Lets a note go, whatever else is going on.
    /// </summary>
    /// <remarks>
    /// None of the guards that stand in front of pressing a key stand here, and
    /// that asymmetry is the point: a key going down can start something, and one
    /// coming up can only ever stop one. Every guard is a way for a release to be
    /// missed, and a missed release is a note that sounds for the rest of the
    /// session. Releasing one that was never played does nothing, which is what
    /// makes ignoring the guards safe.
    /// </remarks>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        midi.KeyUp(e.Key);
    }

    /// <summary>
    /// Whether a keystroke is a plain one, with nothing held that turns a letter
    /// into a command.
    /// </summary>
    /// <remarks>
    /// Shift is deliberately not one of them: it is part of typing a letter, and no
    /// gesture in the shell is Shift and a letter, so a capital Z still plays.
    /// </remarks>
    private static bool Bare(KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;

    /// <summary>
    /// Whether the computer's keyboard is an instrument right now — whether either
    /// of the running programs is reading it.
    /// </summary>
    /// <remarks>
    /// Asked of the compiled programs rather than of the patch, which is what makes
    /// it exact: a MIDI In wired to nothing is read by neither and should not take
    /// keystrokes from the editor, and one wired only to the speakers should. Dead
    /// -code elimination has already answered both (ADR-0022).
    /// </remarks>
    private bool Playing =>
        !Typing
        && (Reads(preview.Program.LiveInputs) || Reads(audio.Live.Keys));

    private static bool Reads(IReadOnlyList<string> inputs) =>
        inputs.Any(key => key.StartsWith(MidiSources.Keyboard + "/", StringComparison.Ordinal));

    /// <summary>
    /// Whether the keystroke belongs to something being typed into rather than to
    /// the instrument.
    /// </summary>
    /// <remarks>
    /// The whole reason the notes can be on bare letters: a text box does not mark
    /// an ordinary key press handled, so without this, naming a patch would play a
    /// tune. AvalonEdit is not a <see cref="TextBox"/> and the focus check never
    /// sees it, so it is asked about separately — and only where the text is the
    /// document, since a printing (ADR-0068) is a reading rather than a place
    /// anybody is composing.
    /// </remarks>
    private bool Typing =>
        FocusManager.GetFocusedElement() is TextBox
        || (showingCode && sourceOwned);

    /// <summary>
    /// One key, as either a note or the pair that moves the two rows. Null-ish by
    /// design: anything that is neither is left alone and goes on meaning
    /// whatever it meant.
    /// </summary>
    private bool PlayKey(Key key)
    {
        if (midi.Shift(key) is { } moved)
        {
            Report(moved);
            return true;
        }

        return midi.KeyDown(key);
    }

    /// <summary>
    /// Greys the two out when there is nothing behind or ahead — the same
    /// question a button would answer by doing nothing, asked where it can be
    /// seen instead — and says in the title what the patch is and whether there
    /// is unsaved work in it.
    /// </summary>
    private void RefreshEditState()
    {
        // Literally the answer the gesture gives, rather than a second statement of
        // the same rule — see UndoLandsOn.
        undoButton.IsEnabled = UndoLandsOn is not null;
        redoButton.IsEnabled = RedoLandsOn is not null;

        // The name first and the program second, which is the way round every
        // other window on the machine says it: what is on screen is the patch,
        // and which program is drawing it is the thing already known.
        var named = patchName is null ? BaseTitle : $"{patchName} — {BaseTitle}";

        // A dot rather than the word, because the title bar is read at a glance
        // and the question it answers is only whether there is anything to lose.
        Title = SomethingToLose ? named + " •" : named;
    }
}
