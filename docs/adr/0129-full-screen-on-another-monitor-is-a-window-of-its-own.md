# ADR-0129: Full screen on another monitor is a window of its own

**Status:** Accepted · 2026-09-21 · *user-directed*

## Context

Double-clicking the preview gives it the whole window: the shell around it is
hidden and the preview is never reparented, because moving an
`OpenGlControlBase` tears its context down and the picture blinks.

Somebody with two monitors wants the picture full screen on one and the editor
on the other, to patch while watching. Moving the main window to the other
monitor would take the editor with it.

## Decision

**Settings → Graphics → Full screen picks the window's monitor, another monitor,
or a chosen one** (`OutputSettings.FullScreen`, `FullScreenMonitor`). Another
monitor is the leftmost the window is not on. A chosen monitor is found the way
the layout's is ([0121](0121-the-window-is-left-as-it-was-left.md)), and is kept
while unplugged. Whenever the answer is the window's own monitor, including a
machine with one, full screen is what it always was.

**On another monitor the preview moves to a borderless full-screen window there**
(`MainWindow.ShowPictureOn`), owned by the main one and not activated, so the
keyboard stays with the editor. Where the preview was, a note says where it went.
Escape in either window, a double-click on the picture, or closing it brings the
preview back.

**A renderer does not survive the move, so the host builds a fresh one each way**
(`PreviewHost.Renew`), carrying the program, clock, size and any recording across
as a backend switch does. The picture blinks once going and once coming back;
feedback history starts from black.

## Consequences

- The transport overlay stays on the main window: on another monitor the editor
  is right there.
- A side panel beside the picture on the other monitor is a second thing to put
  in that window, not a change to this one.
