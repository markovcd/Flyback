# ADR-0217: On a Mac a scroll pans and a pinch zooms

**Status:** Accepted · 2026-11-01 · *user-directed* · amends
[0046](0046-the-module-list-is-a-gesture-not-a-panel.md)'s wheel zoom on a Mac

## Context

The wheel zooms the canvas (ADR-0046). On a MacBook the wheel is two fingers on the
trackpad, so moving the view up and down zoomed it instead, and the momentum macOS adds
after the fingers lift kept zooming. A pinch did nothing, because nothing listened for
it. Mac programs with a canvas, Figma and Maps among them, scroll with two fingers and
zoom with a pinch.

## Decision

**On a Mac, a scroll pans the canvas** by the pixels it scrolled, both ways at once,
momentum and all. **Ctrl or Cmd with a scroll zooms** about the pointer, which is the
wheel zoom a Mac mouse keeps. **A pinch zooms** about the pointer by as much as the
fingers spread, on every platform that reports one.

**Elsewhere the wheel still zooms.** Windows and Linux mice have a middle button to pan
with, and their touchpads already send a pinch as Ctrl and the wheel.

**The host decides**, through `EditorHost.ScrollPans`: the desktop program sets it on
macOS, and the web editor when the browser says it runs on a Mac. A canvas built by a
test zooms unless the test says otherwise, so the suite reads the same on every runner.

**A focused well takes the scroll in whole notches.** A trackpad sends a stroke as dozens
of fractions of a notch; each used to be a whole nudge. Now they add up, and a notch of a
mouse is one nudge as before.

The help lists the scroll and the pinch where a scroll pans.

## Consequences

- A Mac mouse with a wheel pans with it, as it does in other Mac programs, and zooms with
  Cmd held.
- A swipe or a rotation on the trackpad still does nothing on the canvas.
