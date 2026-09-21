namespace Flyback.App;

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
    /// discoverable.
    /// </remarks>
    internal const string Canvas =
        "Right-click the canvas — or press Space — to add a module. "
        + "Type to narrow the list, arrows to move through it, Enter to add.\n\n"
        + "Select a module to edit its values, and double-click its "
        + "name here to call it something else.\n\n"
        + "Open and Save are on the toolbar, and on Ctrl+O and Ctrl+S.\n\n"
        + "The preview size and the renderer are in Settings, on the toolbar.\n\n"
        + "Record, on the toolbar, writes what the patch is doing to a file — "
        + "knobs and all, as it happens. Ctrl+R starts and stops it.\n\n"
        + "Drag from a socket to patch it into another, or onto bare "
        + "canvas to add a module already plugged in.\n"
        + "Drag a connected input to unplug it and take the wire "
        + "somewhere else.\n"
        + "Ctrl+drag an output with one wire on it to feed that "
        + "wire from somewhere else instead.\n"
        + "Drag the background to select, middle-drag to pan, "
        + "wheel to zoom.\n"
        + "Esc backs out of a drag and puts back whatever it had moved.\n"
        + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
        + "Ctrl+C, Ctrl+X and Ctrl+V copy, cut and paste, "
        + "and Ctrl+D duplicates without touching the clipboard.\n"
        + "Ctrl+G draws a selection as one box and Ctrl+Shift+G "
        + "puts it back; double-click a box to open it.\n"
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
    internal const string Locked =
        "The text is the document, and this is a view of what it builds. "
        + "Press F2 to go back to it — modules and wires are added and removed there, "
        + "and \"Edit on the canvas\" under the text hands the patch back so they can be "
        + "drawn here instead.\n\n"
        + "Select a module — on the canvas, or by putting the caret in the code where "
        + "it is written — to edit it here. Its knobs, its tune, its file: letting go "
        + "writes the new value into the code, where the code already says it.\n\n"
        + "Drag the background to select, middle-drag to pan, wheel to zoom.\n"
        + "Ctrl+click adds to a selection, Ctrl+A takes everything.\n"
        + "Ctrl+C copies what is selected, Ctrl+F frames the patch.";

    /// <summary>
    /// What the panel says for a caret standing on a module the patch has moved on
    /// from — see <see cref="MainWindow.Adrift"/>. Said rather than left blank: a panel that
    /// quietly stops cannot be told apart from a caret in the wrong place.
    /// </summary>
    internal const string Adrifting =
        "The text has moved on from the patch that is playing, so this module is not "
        + "there to edit yet — the code names a module by where it stands, and something "
        + "typed in ahead of this one gives it a new name.\n\n"
        + "Apply the text to catch the patch up, or take the edit back. Modules the "
        + "edit did not move are still here to select.";
}
