

namespace Flyback.Editor.Inspect;

/// <summary>What the inspector says with nothing selected, on each kind of canvas.</summary>
internal static class InspectorHelp
{
    /// <summary>
    /// What the panel lists with nothing selected: every gesture the canvas has,
    /// in groups. A page has no files, settings or recording to name, and a finger
    /// has no keys.
    /// </summary>
    /// <param name="dragToPan">Whether the left button pans and the right one selects (ADR-0182).</param>
    internal static IReadOnlyList<HelpGroup> Shortcuts(bool inPage, bool fingers, bool dragToPan = false)
    {
        List<HelpGroup> groups = fingers ? FingerGroups(inPage) : KeyGroups(inPage, dragToPan);
        groups.RemoveAll(g => g.Rows.Count == 0);
        return groups;
    }

    private static HelpRow Row(string title, string detail = "", string keys = "") => new(title, detail, keys);

    private static List<HelpGroup> KeyGroups(bool inPage, bool dragToPan) =>
    [
        new("Getting started",
        [
            Row("Add a module", "Type to narrow the list, arrows to move through it, Enter to add.", "Space / Right-click"),
            Row("Edit a module", "Select it to change its values. Double-click its name in the panel to rename it."),
            Row("Edit as text", "The patch as text to edit, diff and generate. Again shows the canvas.", "F2"),
            Row("Build the text", "Puts the text back on the canvas as a patch.", "Ctrl+Enter"),
            .. inPage ? [] : new[]
            {
                Row("Open", "Also on the toolbar.", "Ctrl+O"),
                Row("Save", "Also on the toolbar.", "Ctrl+S"),
                Row("Preview size and renderer", "In Settings, on the toolbar."),
            },
        ]),
        new(inPage ? "Run" : "Run and record",
        [
            Row("Pause / play", "Pauses the patch, and plays it on.", "Ctrl+P"),
            Row("Knob panel", "Shows the knobs under the canvas, and hides them again.", "Ctrl+K"),
            Row("Randomize the knobs", "Throws the die at the end of the panel.", "Ctrl+Shift+K"),
            .. inPage ? [] : new[]
            {
                Row("Measure", "Runs the patch for a few seconds and pins what each output carries beside it. Again hides them.", "Ctrl+M"),
                Row("Record", "Writes what the patch is doing to a file, knobs and all, as it happens. Starts and stops.", "Ctrl+R"),
            },
        ]),
        new("Patching",
        [
            Row("Patch a wire", "Drag from a socket into another, or onto bare canvas to add a module already plugged in.", "Drag"),
            Row("Unplug", "Drag a connected input to take the wire somewhere else.", "Drag"),
            Row("Re-route an output", "Feeds one of its wires from somewhere else instead. Put it back and the next one takes the next wire.", "Ctrl+Drag"),
            Row("Cancel a drag", "Puts back whatever it had moved.", "Esc"),
            Row("Switch a module off", "Passes what is patched into it straight through. Again switches it on.", "Ctrl+B"),
        ]),
        new("Selecting and moving",
        [
            dragToPan ? Row("Select", "Right-drag the background.", "Right-drag") : Row("Select", "Drag the background.", "Drag"),
            dragToPan ? Row("Pan", "Drag the background.", "Drag") : Row("Pan", keys: "Middle-drag"),
            Row("Zoom", keys: "Wheel"),
            Row("Add to selection", keys: "Ctrl+Click"),
            Row("Select all", keys: "Ctrl+A"),
            Row("Frame the patch", keys: "Ctrl+F"),
            Row("Delete", "Removes what is selected.", "Delete"),
        ]),
        new("Copying and arranging",
        [
            Row("Undo, redo", "Takes the last edit back, and puts it back.", "Ctrl+Z / Ctrl+Y"),
            Row("Copy, cut, paste", keys: "Ctrl+C / Ctrl+X / Ctrl+V"),
            Row("Duplicate", "Without touching the clipboard.", "Ctrl+D"),
            Row("Group", "Draws a selection as one box. Double-click a box to look inside, Esc to put it back.", "Ctrl+G"),
            Row("Ungroup", "Puts the selection back.", "Ctrl+Shift+G"),
            Row("Expand all touched boxes", "Opens every box the selection touches at once.", "Ctrl+E"),
            Row("Collapse", "Shuts them again.", "Ctrl+Shift+E"),
            Row("Lay out selection", "Lays out only what is selected.", "Ctrl+Shift+L"),
            Row("Lay out patch", "Lays out the whole patch.", "Ctrl+L"),
        ]),
    ];

    private static List<HelpGroup> FingerGroups(bool inPage) =>
    [
        new("Getting started",
        [
            Row("Add a module", "Hold a finger on bare canvas, or tap + on the toolbar, to add a module there. Tap the box at the top of the list to narrow it by name."),
            Row("Edit a module", "Tap it to edit its values here; Bypass, Duplicate and Delete are under its name, and More has the rest, renaming it among them."),
            Row("Side button", "In a narrow window, on the toolbar, it shows this panel in the canvas's place, and the canvas again."),
            .. inPage ? [] : new[]
            {
                Row("Open and Save", "On the toolbar."),
                Row("Preview size and renderer", "In Settings, on the toolbar."),
            },
        ]),
        new("Run and record",
        inPage
            ? []
            :
            [
                Row("Measure", "Beside Record, runs the patch for a few seconds and pins what each output carries beside it. Again hides them."),
                Row("Record", "At the right of the row along the foot of the window, writes what the patch is doing to a file, knobs and all, as it happens."),
            ]),
        new("Patching",
        [
            Row("Patch a wire", "Drag from a socket into another, or onto bare canvas to add a module already plugged in. A fingertip beside a socket lands on it."),
            Row("Unplug", "Drag a connected input to take the wire somewhere else."),
            Row("Turn a value", "Hold a finger on an unplugged input, then slide it up or down."),
        ]),
        new("Selecting and moving",
        [
            Row("Move", "Drag a module."),
            Row("Select", "Drag bare canvas."),
            Row("Pan and zoom", "Two fingers drag the view, and spread or pinch to zoom. The frame button on the toolbar fits the whole patch."),
            Row("Hear without", "Hold a finger on a module to hear the patch without it until you let go."),
            Row("Look inside a box", "Double-tap it."),
        ]),
    ];

    /// <summary>
    /// The same, for a canvas that is a view of somebody's source rather than the
    /// patch itself — see ADR-0068. Everything that reads is here and everything
    /// that writes is gone; naming the gestures that are switched off would leave
    /// somebody concluding the program was broken.
    /// </summary>
    internal static string Locked(bool fingers, bool dragToPan = false) =>
        "The text is the document, and this is a view of what it builds. "
        + (fingers ? "Tap the code button, on the toolbar," : "Press F2")
        + " to go back to it — modules and wires are added and removed there, "
        + "and \"Edit on the canvas\" under the text hands the patch back so they can be "
        + "drawn here instead.\n\n"
        + "Select a module — on the canvas, or by putting the caret in the code where "
        + "it is written — to edit it here. Its knobs, its tune, its file: letting go "
        + "writes the new value into the code, where the code already says it.\n\n"
        + (fingers
            ? "Drag bare canvas to select. Two fingers drag the view, and spread or pinch to zoom."
            : (dragToPan
                  ? "Right-drag the background to select, drag it to pan, wheel to zoom.\n"
                  : "Drag the background to select, middle-drag to pan, wheel to zoom.\n")
              + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
              + "Ctrl+C copies what is selected, Ctrl+F frames the patch.");

    /// <summary>
    /// What the panel says for a caret standing on a module the patch has moved on
    /// from — see <see cref="CaretFollow.IsAdrift"/>. Said rather than left blank: a panel that
    /// quietly stops cannot be told apart from a caret in the wrong place.
    /// </summary>
    internal const string Adrifting =
        "The text has moved on from the patch that is playing, so this module is not "
        + "there to edit yet — the code names a module by where it stands, and something "
        + "typed in ahead of this one gives it a new name.\n\n"
        + "Apply the text to catch the patch up, or take the edit back. Modules the "
        + "edit did not move are still here to select.";

    /// <summary>The same, for a caret on a group block the patch has not got yet.</summary>
    internal const string AdriftingGroup =
        "The text has moved on from the patch that is playing, so this group is not "
        + "there to show yet — a group is known by its name, and one named or added "
        + "since the text was applied is not on the canvas.\n\n"
        + "Apply the text to catch the patch up, or take the edit back.";
}
