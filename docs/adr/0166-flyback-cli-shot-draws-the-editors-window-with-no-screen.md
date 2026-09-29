# ADR-0166: flyback-cli shot draws the editor's window with no screen

**Status:** Accepted · 2026-09-29 · *user-directed* · implemented in
`src/Flyback.App/Shots/` and `src/Flyback.Cli/Commands/ShotCommand.cs`

## Context

A picture of the editor's window, as the website's hero is, came from launching the
real window and driving it: a red title bar so nobody clicks it, posted clicks at
screen coordinates, `SendKeys` for Ctrl+F, a sleep until the clock reached the
moment, then `PrintWindow`. The patch played through the speakers the whole time,
and a Debug build that could not keep up with the patch reached half the moment in
the time it was given.

The picture of a patch alone was already a command (`flyback-cli render --at`), and
the canvas alone a headless test (`PatchShotTests`). The window was neither.

## Decision

**`flyback-cli shot` writes a PNG of the editor's window with a patch open.** A file
or `--preset`, `--at` for the second the picture is of, `--size` for the window and
`--select` for a box or module the inspector shows.

**The CLI checks the request and `Flyback --shot` draws it.** The CLI keeps carrying
no Avalonia ([0157](0157-flyback-cli-render-draws-on-the-gpu.md)), so it opens the
patch or finds the preset, saying what is wrong the way every other command does,
then hands a fixed form of the request to the editor beside it, as `flyback-cli
viewer` hands over to the viewer. The editor gains `Avalonia.Headless` and nothing else.

**The window is the program's own, on the headless platform.** The same container
builds it, with nothing read from or kept on the machine (`EditorSetup` with no
paths), the patch opened by the route a launch takes, and Skia drawing it as it
draws a real window. What is swapped:

- the sound, for one that is compiled but never played, whose clock is wherever the
  shot puts it;
- the picture's renderer, for the processor's, since there is no OpenGL;
- the default preset, for the one `--preset` names.

**The picture runs up to the moment a frame at a time.** The clock starts a second
and a half before `--at` and steps a twentieth of a second, each frame drawn before
the next is asked for, so a patch that reads its previous frame has a history, and
the shot is the same on a slow machine as on a fast one. The preview counts the
frames it has put on screen for this.

## Consequences

The hero shot of Flyback Theme is one command, in a few seconds, with no window
opened and nothing heard.

The shot has no title bar: the operating system draws that, and there is none. The
status bar says CPU, since the processor drew the picture, and a Scope or an
Analyzer is empty, since nothing was played for it to chart.

A popup, a dropdown or a dialog is not in a shot; those still come from the real
window (`site-screenshots`).
