# ADR-0165: A finger is a mouse button

**Status:** Accepted · 2026-09-29 · *user-directed* · builds on
[0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md) and
[0046](0046-the-module-list-is-a-gesture-not-a-panel.md)

## Context

With the editor in a page ([0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md)),
it is opened on phones and tablets. Avalonia hands a finger over as the left button,
so a tap selected, a drag carried a module or drew a wire, and a knob turned. What a
hand could not do was everything else the canvas asks of a mouse: pan (the middle
button, [0046](0046-the-module-list-is-a-gesture-not-a-panel.md)), zoom (the wheel),
open the module list (the right button, or Space), turn a socket (the right button
held), and the commands that are keys.

## Decision

**Fingers are translated into the buttons the canvas already answers.** `Fingers`
takes the touches `NodeEditor` receives and calls `CanvasGestures` with a button, a
point and a click count, the same as a mouse does. No gesture learns about fingers.

- **One finger is the left button,** once it moves past a few pixels or lifts. Until
  then it could still be any of the three, so nothing is pressed.
- **A finger held still is the right button:** on bare canvas it opens the module
  list there, on an unpatched input it turns the socket, on a module it holds it.
- **A second finger is the middle button.** Two fingers pan and spread to zoom.
  Under a gesture already going, the second finger pans with it on hold, as the
  middle button does.
- **A fingertip reaches further.** A press or a wire's end near a socket lands on
  it, within 16 screen pixels.
- **What only a key did gets a button.** Duplicate joins the panel's row of actions
  beside group and delete. Adding a module and framing the patch go on the toolbar,
  shown once a finger has touched the canvas.

Nothing anchors the pointer for a finger: a knob, a socket's turn and a part grid's
cell follow it where it goes.

## Alternatives considered

**Avalonia's gesture recognizers** (pinch, scroll, hold). They raise their own
events alongside the pointer's, so each canvas gesture would have to learn to ignore
the press they grew out of. Translating before the canvas keeps one path.

**A tap then a drag for panning.** One finger already means a rubber band on bare
canvas, and a band is the only way a hand selects more than one module.

## Consequences

- Ctrl and Shift have no finger: adding to a selection, lifting a wire off an
  output, fine turns and moving modules between groups are a mouse's.
- Hover says nothing to a finger, so a socket's tip is not reached by touch.
- A tap selects on lifting rather than on landing.
