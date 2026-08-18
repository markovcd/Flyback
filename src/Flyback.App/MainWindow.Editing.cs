using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;

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

    /// <summary>
    /// The three answers, as a window rather than as a system message box —
    /// there is no such thing here, and one built by hand is the same three
    /// buttons in the same palette as the rest of the shell.
    /// </summary>
    /// <remarks>
    /// Closing it by its own frame is Cancel, which is the answer that loses
    /// nothing. That is why Cancel is the enum's default as well: an answer
    /// nobody gave should never be the destructive one.
    /// </remarks>
    private async Task<Unsaved> AskAboutUnsavedAsync()
    {
        var answer = Unsaved.Cancel;
        Window? dialog = null;

        Button Answering(string text, Unsaved with, bool wide = false)
        {
            var button = new Button { Content = text, MinWidth = wide ? 120 : 96 };

            button.Click += (_, _) =>
            {
                answer = with;
                dialog?.Close();
            };

            return button;
        }

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

        dialog = Dialog.Around("Unsaved changes", asking);

        await dialog.ShowDialog(this);

        return answer;
    }

    /// <summary>
    /// Nothing may block inside a closing handler, so a window with unsaved work
    /// in it cancels the close, asks, and closes itself again on the way back.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || leaving || !editor.IsModified) return;

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
        }
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
