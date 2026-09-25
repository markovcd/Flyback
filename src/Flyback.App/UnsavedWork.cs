using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.Core;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// What closing or replacing the patch would lose, and the question every route out
/// of a patch asks first: open, a preset, a restart and the window's own close.
/// </summary>
/// <remarks>
/// One place fronts every way a patch can be closed, so none of those callers has to
/// know whether anything was edited. Saving lives here too, because Save… is one of
/// the answers.
/// </remarks>
internal sealed class UnsavedWork(
    Shell shell,
    PatchFiles files,
    Lazy<TakeRecording> recording,
    EditorSetup setup,
    IDialogs dialogs,
    IWindowClose window)
{
    /// <summary>What to do about a patch that has been edited and not written out.</summary>
    private enum Unsaved
    {
        /// <summary>Refused, and whatever asked should not go ahead. Also what a dialog closed by its frame returns.</summary>
        Cancel,

        Save,

        Discard,
    }

    private CanvasHistory History => shell.Editor.History;

    private Document Document => shell.Document;

    /// <summary>
    /// Set while the question is on the screen. The dialog is a panel over the window,
    /// so the frame's cross stays live under it, and a second close arriving meanwhile
    /// is refused rather than stacking a second copy of the question.
    /// </summary>
    public bool Asking { get; private set; }

    /// <summary>
    /// Set once the question has been answered, or needs no asking, so the close that
    /// follows goes through: nothing may block inside a closing handler, so the way out
    /// is to cancel, ask, and close again.
    /// </summary>
    public bool Leaving { get; private set; }

    /// <summary>
    /// Whether the document has anything in it closing would lose: an edit, typing not
    /// yet applied, or a conversation nobody has saved (ADR-0072).
    /// </summary>
    public bool SomethingToLose =>
        History.IsModified || Document.IsUnapplied || shell.Assistant?.ConversationUnsaved == true;

    /// <summary>Lets the next close through without asking.</summary>
    public void Leave() => Leaving = true;

    /// <summary>
    /// Whether the thing about to replace or close the patch may go ahead. Asks only
    /// where there is something to lose.
    /// </summary>
    public async Task<bool> MayReplaceThePatchAsync()
    {
        if (!SomethingToLose) return true;

        return await AnsweredAsync(
            "Unsaved changes",
            History.IsModified || Document.IsUnapplied
                ? "This patch has changes that have not been saved. Closing it now would lose them."
                : "The conversation about this patch has not been saved. Closing it now would lose it.");
    }

    /// <summary>
    /// Whether text about to stop being the document may go. The patch is not asked
    /// about: handing it back to the canvas changes who owns it, not what it is.
    /// </summary>
    public async Task<bool> MayLoseTheTextAsync()
    {
        if (!Document.IsUnapplied) return true;

        return await AnsweredAsync(
            "Unsaved text",
            "This text has not been saved. Handing the patch back to the canvas empties it, "
            + "and its comments, its names and its defs go with it — the patch itself is "
            + "untouched.");
    }

    /// <summary>
    /// The Save gesture whole: the picker, then the write.
    /// </summary>
    /// <returns>Whether the document was saved. A canceled picker is not a save, and nor is a copy.</returns>
    public async Task<bool> SavePatchAsync() =>
        await files.PickSaveAsync() is { } file && await SaveToAsync(file);

    /// <summary>Writes the document to a file the picker handed back, as the kind its name says.</summary>
    /// <returns>
    /// Whether the document was saved, which writing a file is, except where the file is
    /// a printing of a patch the graph owns: that is a copy, and leaves what was unsaved
    /// as unsaved as it was.
    /// </returns>
    public async Task<bool> SaveToAsync(IStorageFile file)
    {
        if (PatchFileKinds.Sourced(file.Name)) return await files.SaveSourceAsync(file) && !SomethingToLose;

        // Either of the other two hands the patch to the graph and empties the text,
        // so text that is nowhere else is asked about first (ADR-0068).
        return await MayLoseTheTextToAsync(file.Name) && await files.SavePatchFileAsync(file);
    }

    /// <summary>
    /// Closes the window, asking about unsaved work as any close does, and starts
    /// Flyback again behind it, opening <paramref name="reopen"/>. False where the
    /// window stays: the question was canceled, or a take is running, which only its
    /// own button should end.
    /// </summary>
    public async Task<bool> RelaunchAsync(Reopen? reopen)
    {
        if (setup.Relaunch is not { } relaunch) return false;

        if (recording.Value.InHand || !await MayReplaceThePatchAsync()) return false;

        relaunch(reopen);

        Leaving = true;
        window.Close();

        return true;
    }

    /// <summary>The document as a crash would lose it, or null while there is nothing to lose (ADR-0103).</summary>
    public RecoveredWork? Work() => !SomethingToLose ? null : new(
        files.Name,
        files.SoundFolder.Beside,
        PatchIO.ToJson(History.Patch),
        Document.Owned ? Document.Text : null,
        shell.Assistant?.ConversationToSave(),
        files.Carried?.Bytes);

    /// <summary>
    /// Whether a save that makes <paramref name="name"/> the document may empty text
    /// written nowhere else. Two answers rather than three, since Save… is how this was
    /// reached, and its own dialog, since the unsaved question's may be down by now.
    /// </summary>
    private async Task<bool> MayLoseTheTextToAsync(string name)
    {
        if (!Document.IsUnapplied) return true;

        var was = Asking;
        Asking = true;

        try
        {
            return await AskAsync(
                "Unsaved text",
                $"Saving as {name} makes the canvas the document and empties this text, which has "
                + "not been saved: its comments, its names and its defs go with it. Save it as "
                + $"{GlobalConstants.ApplicationName} text to keep them.",
                discard: "Save without the text",
                offerSave: false) == Unsaved.Discard;
        }
        finally
        {
            Asking = was;
        }
    }

    /// <summary>Puts the three answers up and does what the answer says.</summary>
    private async Task<bool> AnsweredAsync(string about, string question)
    {
        // Refused rather than queued behind the answer already on the screen.
        if (Asking) return false;

        Asking = true;

        try
        {
            return await AskAsync(about, question) switch
            {
                // A canceled save picker is a canceled close: somebody who thought
                // better of where has not agreed to lose the patch.
                Unsaved.Save => await SavePatchAsync(),
                Unsaved.Discard => true,
                _ => false,
            };
        }
        finally
        {
            Asking = false;
        }
    }

    /// <param name="discard">What the answer that goes ahead is called.</param>
    /// <param name="offerSave">Whether saving is one of the answers, which it is not where saving is what asked.</param>
    private async Task<Unsaved> AskAsync(
        string about,
        string question,
        string discard = "Discard changes",
        bool offerSave = true)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        if (offerSave) buttons.Children.Add(Answering("Save…", Unsaved.Save));

        buttons.Children.Add(Answering(discard, Unsaved.Discard, wide: true));
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

        return await dialogs.Show<Unsaved>(about, asking);

        static Button Answering(string text, Unsaved with, bool wide = false)
        {
            var button = new Button { Content = text, MinWidth = wide ? 120 : 96 };
            button.Click += (_, _) => Dialog.Close(button, with);

            return button;
        }
    }
}
