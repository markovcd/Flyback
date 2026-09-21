# ADR-0123: A third program plays a patch and writes nothing

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

There was no cheap way to see or hear a patch. Opening the editor brings the whole
editor, and `flyback-cli render` writes a file, which for ten seconds of 1080p is a
wait measured in minutes. Somebody who only wants to know what a patch looks and
sounds like, an agent especially, had neither.

## Decision

**`flyback-viewer` opens a patch and plays it, picture and sound, and nothing else**
(`src/Flyback.Viewer`). A program of its own rather than a mode of the editor,
because what the editor keeps is exactly what a look must not leave behind.

**It writes nothing.** No settings, layout, recovery file or statistics, and it
never touches the patch. It reads the plugin folder and `output.json`, and its
defaults are that file's: size, renderer, preview rate, latency, and the startup
preset when no patch is named. Every one is overridable on the command line, and
`--settings` names another file, so `--help` prints what this machine is set to.

**A patch comes from a path or a preset name.** A document, a bundle or a patch
written as text opens through `PatchFile`, the loader the CLI uses; `--preset` takes
a shipped preset or one saved in the gallery. A patch with holes in it says so on
stderr and plays anyway. Nothing to play exits with 2.

**`--background` and `--hidden` keep a look from landing on whoever is using the
machine.** `--background` shows the window without activating it. `--hidden` opens
no window: the patch plays and nothing appears, so it implies `--no-video`, and it
is refused alongside any flag that shapes a window.

**A patch that is played is played here.** The computer's keys, the MIDI device a
MIDI In names, and the controller a panel knob is bound to reach the running
programs through the editor's own `MidiHub` and `ControlHub`, which live in
`Flyback.Ui` for that reason. Without it a preset made to be performed, Dub or
Dodge, opened as its backing track and nothing else. A knob with no controller
stays where the patch left it: there is no panel, and a panel is editing. Letters
are notes only while a running program reads the keyboard, Space pauses because no
layout plays it, and Ctrl+P does because the editor's does. A `--hidden` run has no
window and so no keys, and still hears its MIDI devices.

**The full screen is not the editor's.** A double-click on the picture takes the
screen and a double-click or Escape gives it back. The editor's version zeroes grid
tracks around a preview that must not be reparented; the viewer has no tracks, and
the two are not to be unified.

**The editor does not gain a mute.** The viewer's `--volume` and `--mute` are a gain
after the mix (`AudioEngine.Gain`), after the recording's tap so a take keeps what the
patch made. In the editor the Output's Volume is the switch
([0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md)), and a second one would be two truths about
whether sound is on.

**It writes no frame.** `flyback-cli render -o shot.png --at 3` already renders a
still headless and quickly, and a second stills exporter here would be the
duplication [0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)
removed.

**The CLI reaches it by starting it.** `flyback-cli viewer` hands every token to
`flyback-viewer` beside it and returns its exit code, so its `--help` is the
viewer's own and a new flag needs nothing in the CLI, which goes on carrying no UI
framework.

## Consequences

- The publish folder holds three programs on one runtime, and the Dockerfile
  publishes the third beside the other two.
- The viewer declares no `PluginProject` items, for the CLI's reason: the target
  that lays the plugin folder out clears it first. Run from its own build output it
  has no sound backend and says so.
- The patch is compiled once. Nothing in the viewer reacts to an edit, because
  there is nowhere to make one. The hubs are pointed at the two programs once, for
  the same reason.
- A window shown with `--background` does not take the keyboard, so a look an agent
  takes plays no notes by accident.
- `--size` honors what `output.json` says as written; the editor snaps a saved size
  to a row of its list. For anything the editor wrote they agree.
