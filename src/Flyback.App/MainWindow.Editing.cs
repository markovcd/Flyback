using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// The editing session as opposed to the patch: undo and redo from wherever the
/// focus is, what the title bar says about unsaved work, and the question every
/// route out of a patch has to ask first.
/// </summary>
/// <remarks>
/// The canvas owns the history and answers whether there is anything to lose;
/// what is here is the asking. One method fronts every way a patch can be
/// closed — quitting, opening a file, picking a preset, taking one from the
/// assistant — so none of those callers has to know whether anything was edited.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>The window title, before anything is said about the patch in it.</summary>
    private const string BaseTitle = "Flyback";

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
        if (!editor.IsModified) return true;

        // The same question is already up. Whatever asked again is refused
        // rather than queued behind the first answer: it is the same patch and
        // the same three buttons, and one set of them is already on the screen.
        if (questionIsUp) return false;

        questionIsUp = true;

        try
        {
            return await AskAboutUnsavedAsync() switch
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
    /// The three answers, as a window rather than as a system message box —
    /// there is no such thing here, and one built by hand is the same three
    /// buttons in the same palette as the rest of the shell.
    /// </summary>
    /// <remarks>
    /// Closing it by its own frame is Cancel, which is the answer that loses
    /// nothing. That is why Cancel is the enum's default as well: a dialog closed
    /// without setting a result comes back as <c>default</c>, so the answer
    /// nobody gave is the harmless one by the language's own rule rather than by
    /// a line of code remembering to make it so.
    /// </remarks>
    private async Task<Unsaved> AskAboutUnsavedAsync()
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
                    Text = "This patch has changes that have not been saved. "
                        + "Closing it now would lose them.",
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        return await this.ShowDialog<Unsaved>("Unsaved changes", asking);

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

        if (!editor.IsModified) return;

        e.Cancel = true;

        if (!await MayReplaceThePatchAsync()) return;

        leaving = true;
        Close();
    }

    /// <summary>

    /// Undo and redo, from wherever the focus happens to be. Handled on the
    /// window rather than on the canvas because an edit is as likely to have
    /// been made in the inspector as on it, and a shortcut that worked only
    /// while the canvas had the focus would be one somebody learns not to
    /// trust. Anything that already dealt with the key keeps it — a text box
    /// undoing its own typing is doing the same job at its own scale.
    /// </summary>
    /// <remarks>
    /// Command as well as Control, so the shortcut is the one the machine uses:
    /// Ctrl+Z on Windows and Linux, Cmd+Z on a Mac. Both are accepted
    /// everywhere rather than asked which platform this is, since neither is a
    /// gesture anything else here claims.
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
            case Key.Z:
                if (again) editor.Redo();
                else editor.Undo();
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                editor.Redo();
                e.Handled = true;
                break;

            // Lay out. Beside the two above because it is the same kind of
            // thing: an edit to the patch that Ctrl+Z takes off again.
            case Key.L:
                editor.Tidy();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Lets a note go, whatever else is going on.
    /// </summary>
    /// <remarks>
    /// None of the guards that stand in front of pressing a key stand here, and
    /// that asymmetry is the point: a key going down can start something and so
    /// has to be sure it was meant, and a key coming up can only ever stop one.
    /// Every guard is a way for a release to be missed, and a missed release is
    /// a note that sounds for the rest of the session.
    /// <para>
    /// So a modifier taken hold of while a key is down, a module deleted, a
    /// device picked, or a text box clicked into mid-note all end the note rather
    /// than stranding it. Releasing one that was never played does nothing, which
    /// is what makes ignoring the guards safe. Nothing is marked handled, because
    /// nothing else in the shell listens for a key coming up.
    /// </para>
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
    /// Shift is deliberately not one of them. It is part of typing a letter
    /// rather than a way of asking for something else — no gesture in the shell
    /// is Shift and a letter on its own — so a capital Z is still a Z and still
    /// plays. Ctrl, Cmd and Alt all mean the keystroke was aimed somewhere else.
    /// </remarks>
    private static bool Bare(KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;

    /// <summary>
    /// Whether the computer's keyboard is an instrument right now — whether, in
    /// other words, either of the running programs is reading it.
    /// </summary>
    /// <remarks>
    /// Asked of the compiled programs rather than of the patch, and that is what
    /// makes it exact rather than nearly right. A MIDI In sitting on the canvas
    /// wired to nothing is not read by either program, so it should not be taking
    /// keystrokes away from the editor; one wired only to the speakers is read by
    /// the audio program and not the picture's, and it should. Dead-code
    /// elimination has already answered both questions (ADR-0022), and asking the
    /// patch would be a second, worse answer to them.
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
    /// The whole reason the notes are on bare letters and can still be. A text
    /// box does not mark an ordinary key press handled — what it acts on is the
    /// text input that follows — so without this, naming a patch would play a
    /// tune, and every letter of the name would be a note nobody could stop.
    /// </remarks>
    private bool Typing => FocusManager?.GetFocusedElement() is TextBox;

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
    /// seen instead — and says in the title whether there is unsaved work.
    /// </summary>
    private void RefreshEditState()
    {
        undoButton.IsEnabled = editor.CanUndo;
        redoButton.IsEnabled = editor.CanRedo;

        // A dot rather than the word, because the title bar is read at a glance
        // and the question it answers is only whether there is anything to lose.
        Title = editor.IsModified ? BaseTitle + " •" : BaseTitle;
    }
}
