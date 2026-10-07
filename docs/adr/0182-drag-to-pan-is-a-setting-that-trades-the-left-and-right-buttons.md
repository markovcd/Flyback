# ADR-0182: Drag to pan is a setting that trades the left and right buttons

**Status:** Accepted · 2026-10-07 · *user-directed* · a second scheme beside
[0046](0046-the-module-list-is-a-gesture-not-a-panel.md)'s middle-button pan

## Context

Panning is the middle button (ADR-0046). A musician trying the editor on a
MacBook found no way to move the view: a trackpad has no middle button, a Mac
mouse rarely has a usable one, and a trackball's wheel is stiff to press. In the
music software they know, dragging empty canvas moves it, and moving around a
patch matters more than selecting in it.

## Decision

**A Canvas setting, Drag empty canvas to pan, off by default.** On, a mouse's
buttons on empty canvas trade places:

- **Left-drag pans.** A left click that never moves clears the selection, as a
  band over nothing did; a drag keeps it, so a selection can be panned beside.
- **Right-drag draws the rubber band**, Ctrl adding to the selection as before.
- **A right click opens the module list**, on the release rather than the press,
  since the press cannot yet tell a click from a band. A press that wanders more
  than a few pixels is a band.

**Only empty canvas changes.** Everything a press lands on first keeps its
button: sockets, modules, boxes, group strips, remap marks and a knob being
linked on the left; the socket dial and hold-to-hear on the right. The middle
button still pans, and the wheel still zooms.

**A finger is untouched.** Touch has its own scheme (one finger selects, two
pan, a held one opens the list) and the setting is read only for a mouse or pen.

**The help follows it.** The empty inspector names the gestures in use, and is
rebuilt when the setting is saved.

**A page gets it too, on a panel of its own.** The web editor is where a newcomer
on a laptop meets Flyback first, and it had no settings at all
([0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md)). Its toolbar gains the gear, which opens a
small panel off the button rather than the desktop's tabbed window: drag to pan
and compact modules, each applied and kept as it is ticked, with no Save. Nothing
about plugins, since a page ships none of its own. The browser's local storage
keeps them, behind `IBrowserStore`; a browser that keeps nothing simply forgets.
Once a finger has touched the canvas, drag to pan leaves the panel.

## Consequences

**Two schemes to keep working.** Every empty-canvas gesture is tested in both,
in `MarqueeSelectTests` and `DragToPanTests`.

**The list opens a beat later in this mode**, on the release. That is the price
ADR-0046 avoided by giving the right button one meaning; here it has two.
