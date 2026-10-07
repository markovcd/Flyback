using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Inspect;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Ui.Midi;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor;

/// <summary>
/// The editor as one control: the regions its container composes (ADR-0150), laid out,
/// and the keys and dropped files it answers wherever the focus is. A desktop window
/// or a page holds it (ADR-0162).
/// </summary>
internal sealed class EditorView : Border
{
    private readonly IEnumerable<ISettingsSection> settingsSections;
    private readonly IDialog dialog;
    private readonly Document document;
    private readonly PatchOpening patchOpening;
    private readonly PresetSlot presets;
    private readonly PanelKnobs knobs;
    private readonly KnobRandomizer randomizer;
    private readonly ReportLine report;
    private readonly Playback playback;
    private readonly EditorStart editorStart;
    private readonly MidiHub midi;
    private readonly FullScreenPreview fullScreen;
    private readonly TransportControls transport;
    private readonly EditState editState;
    private readonly Reactions reactions;
    private readonly WindowHolder holder;
    private readonly bool inPage;

    private TopLevel? host;
    private bool started;

    public EditorView(
        Document document,
        PatchOpening patchOpening,
        ReportLine report,
        MidiHub midi,
        Playback playback,
        PanelKnobs knobs,
        KnobRandomizer randomizer,
        PresetSlot presets,
        Reactions reactions,
        EditorStart editorStart,
        FullScreenPreview fullScreen,
        TransportControls transport,
        ShellLayout shell,
        EditState editState,
        IEnumerable<ISettingsSection> settingsSections,
        IDialog dialog,
        WindowHolder holder,
        EditorHost editorHost)
    {
        inPage = editorHost.InPage;
        this.document = document;
        this.patchOpening = patchOpening;
        this.report = report;
        this.midi = midi;
        this.playback = playback;
        this.editorStart = editorStart;
        this.settingsSections = settingsSections;
        this.knobs = knobs;
        this.randomizer = randomizer;
        this.presets = presets;
        this.fullScreen = fullScreen;
        this.transport = transport;
        this.editState = editState;
        this.dialog = dialog;
        this.reactions = reactions;
        this.holder = holder;

        Background = new SolidColorBrush(Colors.Window);

        // Before the layout, because these are live from the moment the editor
        // is: the preview needs its resolution and its backend whether or not
        // anybody has selected the Output to look at them.
        InitializeOutputControls();

        Child = shell.Build();
    }

    /// <summary>
    /// Makes <paramref name="top"/> the window or page this editor answers for: its
    /// dialogs, keys and dropped files. Once, before <see cref="Start"/>.
    /// </summary>
    public void Hold(TopLevel top)
    {
        if (host is not null) return;

        host = top;
        holder.Attach(top);

        // The popups behind the report and a module's name hang off the top level
        // rather than off this control, so what they look like is said there.
        top.Styles.Add(ReportLine.Trim());
        top.Styles.Add(ModulePlate.Naming());
        top.Styles.Add(ModulePalette.Trim());

        // On the top level, so a key reaches them whatever has the focus, or nothing does.
        top.AddHandler(KeyDownEvent, OnKeyDown);
        top.AddHandler(KeyUpEvent, OnKeyUp);

        // Live from here rather than from Loaded: a drop arriving before the
        // layout has finished is still a drop.
        WireFileDrop(top);
    }

    /// <summary>
    /// Brings the editor up: the saved layout, the first patch and everything that
    /// compiling it starts. Once, after <see cref="Hold"/>; the constructor only wires.
    /// </summary>
    public void Start()
    {
        if (started || host is null) return;
        started = true;

        editState.Refresh();
        editorStart.Start(host);
    }

    /// <summary>
    /// Takes the selection off the preset list, for a document that arrived by some
    /// other route — a patch, a bundle, or a source file.
    /// </summary>
    /// <remarks>
    /// A preset left highlighted would claim the canvas still held it. Setting the
    /// index to -1 is enough: the picker's own handler returns on a negative index
    /// before it asks what "wanted" means.
    /// </remarks>
    internal void ClearPresetSelection() => presets.Clear();

    /// <summary>
    /// Undo and redo, from wherever the focus happens to be. Handled on the top level
    /// rather than on the canvas because an edit is as likely to have been made in
    /// the inspector, and anything that already dealt with the key keeps it — a
    /// text box undoing its own typing is doing the same job at its own scale.
    /// </summary>
    /// <remarks>
    /// Command as well as Control, so the shortcut is the one the machine uses.
    /// Both are accepted everywhere rather than asked which platform this is, since
    /// neither is a gesture anything else here claims.
    /// </remarks>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // A dialog lets the keys typed into its own boxes through unhandled, so
        // whatever it is over must not act on them.
        if (e.Handled || dialog.IsShowing) return;

        // Before the modifier check, because Escape carries none. Only while the
        // picture is full screen: everywhere else Escape belongs to the module
        // filter, which handles its own before this is ever reached.
        if (e.Key == Key.Escape && fullScreen.IsAway)
        {
            fullScreen.Leave();
            e.Handled = true;
            return;
        }

        if (e is { Key: Key.F3, KeyModifiers: KeyModifiers.None } && fullScreen.IsAway)
        {
            transport.ToggleStats(fullScreen.IsFullScreen);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && knobs.StopModes())
        {
            report.Say("Done.");
            e.Handled = true;
            return;
        }

        // Before the instrument, because F2 is not a note and never will be:
        // the keyboard-as-instrument maps letters, and a function key is free
        // for the shell in a way no letter is any more.
        if (e.Key == Key.F2)
        {
            reactions.Raise(new CodeAsked(!document.ShowingCode));
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
                if (again) reactions.Raise(new RedoAsked());
                else reactions.Raise(new UndoAsked());
                e.Handled = true;
                break;

            // The other half of the convention Windows carries: Ctrl+Y is redo
            // where Ctrl+Shift+Z is, and somebody who reaches for one is not
            // going to enjoy discovering which this program wanted.
            case Key.Y:
                reactions.Raise(new RedoAsked());
                e.Handled = true;
                break;

            // Lay out. Beside the two above because it is the same kind of
            // thing: an edit that Ctrl+Z takes off again — the modules across
            // the canvas, or the lines down the page. With Shift, only the
            // selected modules move (ADR-0110).
            case Key.L:
                reactions.Raise(new TidyAsked(OnlySelected: again));
                e.Handled = true;
                break;

            // Not an edit — nothing here is on either undo stack — but routed
            // through the same dispatch as the rest of the toolbar's
            // shortcuts, and guarded the same way a click on a disabled
            // button already is: see TakeRecording.ToggleAsync.
            case Key.R:
                if (!inPage) reactions.Raise(new RecordAsked());
                e.Handled = true;
                break;

            case Key.M:
                if (!inPage) reactions.Raise(new MeasureAsked());
                e.Handled = true;
                break;

            // The panel has no room while the picture has the window; randomizing needs none.
            case Key.K:
                if (again) randomizer.Roll();
                else if (!fullScreen.IsFullScreen) reactions.Raise(new KnobsAsked(!knobs.View.IsVisible));

                e.Handled = true;
                break;

            // With Ctrl because the bare letter is a note, and Space adds a module.
            case Key.P:
                reactions.Raise(new PauseAsked());
                e.Handled = true;
                break;

            // The document itself, on the letters every program uses for it.
            // Both were the toolbar's alone, and the hand that has just
            // finished an edit is on the keyboard rather than the pointer.
            // Saving is one gesture here — the picker is where a name is
            // chosen — so there is no second key for saving under another one.
            case Key.O:
                if (!inPage) reactions.Raise(new OpenAsked());
                e.Handled = true;
                break;

            case Key.S:
                if (!inPage) reactions.Raise(new SaveAsked());
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
    private void OnKeyUp(object? sender, KeyEventArgs e) => midi.KeyUp(e.Key);

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

    /// <summary>Whether the computer's keyboard is an instrument right now, and nothing is being typed.</summary>
    private bool Playing => !Typing && playback.Keyed;

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
        host?.FocusManager?.GetFocusedElement() is TextBox
        || document is { ShowingCode: true, Owned: true };

    /// <summary>
    /// One key, as either a note or the pair that moves the two rows. Null-ish by
    /// design: anything that is neither is left alone and goes on meaning
    /// whatever it meant.
    /// </summary>
    private bool PlayKey(Key key)
    {
        if (midi.Shift(key) is { } moved)
        {
            report.Say(moved);
            return true;
        }

        return midi.KeyDown(key);
    }

    /// <summary>
    /// Lets a patch, a bundle or a text file be opened by dropping it in from
    /// the file explorer — the same three kinds the picker offers, arriving
    /// without one.
    /// </summary>
    private void WireFileDrop(TopLevel top)
    {
        DragDrop.SetAllowDrop(top, true);

        // Refused under a dialog, and shown as refused, for the reason
        // OpenActivatedFileAsync gives.
        top.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !dialog.IsShowing
                ? DragDropEffects.Copy
                : DragDropEffects.None);

        top.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            // Only the first: one editor holds one patch, and a picker never
            // offers more than that either.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file) return;

            e.Handled = true;

            await patchOpening.OpenActivatedFileAsync(file);
        });
    }

    /// <summary>
    /// Called once, from the constructor: what was last saved has to be in force
    /// before anybody has looked.
    /// </summary>
    private void InitializeOutputControls()
    {
        // Quietly, because nobody asked for anything yet: a saved answer is
        // what the program starts in, not a change to report.
        foreach (var section in settingsSections) section.Start();
    }
}
