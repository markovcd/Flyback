

namespace Flyback.Editor.Inspect;

/// <summary>What the inspector says with nothing selected, on each kind of canvas.</summary>
internal static class InspectorHelp
{
    /// <summary>
    /// What the panel says with nothing selected, which is where every gesture
    /// the canvas has is written down.
    /// </summary>
    /// <remarks>
    /// Adding a module is named first because the list opens where it is asked
    /// for, and the Output sits behind the preview so the controls stay
    /// discoverable. A page has no files, settings or recording to name.
    /// </remarks>
    internal static string Canvas(bool inPage, bool fingers) =>
        fingers
            ? FingerAdding + (inPage ? "" : Program(keys: false)) + FingerGestures
            : Adding + (inPage ? "" : Program(keys: true)) + "Ctrl+P pauses the patch, and plays it on.\n\n" + Gestures;

    private const string Adding =
        "Right-click the canvas — or press Space — to add a module. "
        + "Type to narrow the list, arrows to move through it, Enter to add.\n\n"
        + "Select a module to edit its values, and double-click its "
        + "name here to call it something else.\n\n";

    /// <summary>The files, the settings and recording, which a page has none of; their keys only where there is a keyboard to name.</summary>
    private static string Program(bool keys) =>
        (keys ? "Open and Save are on the toolbar, and on Ctrl+O and Ctrl+S.\n\n" : "Open and Save are on the toolbar.\n\n")
        + "The preview size and the renderer are in Settings, on the toolbar.\n\n"
        + (keys
            ? "Measure, beside Record and on Ctrl+M, runs the patch for a few seconds and pins what each output carries beside it; again hides them.\n\n"
            : "Measure, beside Record, runs the patch for a few seconds and pins what each output carries beside it; again hides them.\n\n")
        + "Record, at the right of the row along the foot of the window, writes what the patch is doing to a file — "
        + (keys ? "knobs and all, as it happens. Ctrl+R starts and stops it.\n\n" : "knobs and all, as it happens.\n\n");

    /// <summary>How a finger adds and edits a module, once one has touched the canvas.</summary>
    private const string FingerAdding =
        "Hold a finger on bare canvas, or tap + on the toolbar, to add a module there. "
        + "Tap the box at the top of the list to narrow it by name.\n\n"
        + "Tap a module to edit its values here; Duplicate, Group and Delete are beside its "
        + "name, and a double-tap on the name calls it something else. Hold a finger on a "
        + "module to hear the patch without it until you let go.\n\n"
        + "In a narrow window the side button, on the toolbar, shows this panel in the "
        + "canvas's place, and the canvas again.\n\n";

    private const string FingerGestures =
        "Drag from a socket to patch it into another, or onto bare canvas to add a module "
        + "already plugged in. A fingertip beside a socket lands on it.\n"
        + "Drag a connected input to unplug it and take the wire somewhere else.\n"
        + "Hold a finger on an unplugged input, then slide it up or down to turn its value.\n"
        + "Drag a module to move it, or bare canvas to select.\n"
        + "Two fingers drag the view, and spread or pinch to zoom; the frame button on the "
        + "toolbar fits the whole patch.\n"
        + "Double-tap a box to look inside.";

    private const string Gestures =
        "Drag from a socket to patch it into another, or onto bare "
        + "canvas to add a module already plugged in.\n"
        + "Drag a connected input to unplug it and take the wire "
        + "somewhere else.\n"
        + "Ctrl+drag an output to feed one of its wires from somewhere "
        + "else instead; put it back and the next Ctrl+drag takes the next wire.\n"
        + "Drag the background to select, middle-drag to pan, "
        + "wheel to zoom.\n"
        + "Esc backs out of a drag and puts back whatever it had moved.\n"
        + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
        + "Ctrl+C, Ctrl+X and Ctrl+V copy, cut and paste, "
        + "and Ctrl+D duplicates without touching the clipboard.\n"
        + "Ctrl+G draws a selection as one box and Ctrl+Shift+G "
        + "puts it back; double-click a box to look inside, Esc to put it back.\n"
        + "Ctrl+E opens every box the selection touches at once, "
        + "Ctrl+Shift+E shuts them again.\n"
        + "Ctrl+Shift+L lays out only what is selected, where "
        + "Ctrl+L lays out the whole patch.\n"
        + "Delete removes what is selected, Ctrl+F frames the patch.";

    /// <summary>
    /// The same, for a canvas that is a view of somebody's source rather than the
    /// patch itself — see ADR-0068. Everything that reads is here and everything
    /// that writes is gone; naming the gestures that are switched off would leave
    /// somebody concluding the program was broken.
    /// </summary>
    internal static string Locked(bool fingers) =>
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
            : "Drag the background to select, middle-drag to pan, wheel to zoom.\n"
              + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
              + "Ctrl+C copies what is selected, Ctrl+F frames the patch.");

    /// <summary>
    /// What the panel says for a caret standing on a module the patch has moved on
    /// from — see <see cref="Document.IsAdrift"/>. Said rather than left blank: a panel that
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
