# ADR-0226: On a Mac a mouse wheel zooms and a trackpad pans

**Status:** Accepted · 2026-11-05 · *user-directed* · amends
[0217](0217-on-a-mac-a-scroll-pans-and-a-pinch-zooms.md)'s "a Mac mouse with a wheel pans"

## Context

ADR-0217 made every scroll on a Mac pan the canvas, so two fingers on a trackpad would
move the view rather than zoom it. A mouse wheel got caught too: it panned, and zooming
took Cmd. Node editors (Blender's, Unreal's Blueprints, Houdini) zoom with the wheel on
every platform. Design tools such as Figma pan with it. Miro tells the two devices
apart and has a setting for when it guesses wrong.

## Decision

**On a Mac a mouse wheel zooms and a trackpad pans.** Each scroll is judged at its first
notch and keeps that judgment until it rests, so a trackpad's momentum never turns into
a zoom halfway. On the desktop, AppKit says whether the scroll has precise deltas, which
a trackpad or a Magic Mouse has and a wheel that clicks does not. In a browser a
trackpad's legacy `wheelDeltaY` is exactly three times its `deltaY`, and a wheel's is
not; Firefox counts a wheel in lines. A scroll the host cannot place pans, as under 0217.
Ctrl or Cmd with any scroll zooms, and a pinch zooms, as before.

**Settings → Canvas → Scroll overrides the guess**: Automatic, Pans the view (every
scroll pans, Ctrl or Cmd zooms) or Zooms the view (every scroll zooms). The web page's
settings panel offers the same list. Automatic is the default, and on Windows and Linux
it is the wheel zoom it always was.

## Consequences

- A Magic Mouse scrolls with precise deltas, so it pans like a trackpad; Zooms the view
  is the way to make it zoom.
- A browser that changes how it reports a wheel can be misread. The setting is the way out.
