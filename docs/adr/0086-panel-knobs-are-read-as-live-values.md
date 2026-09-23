# ADR-0086: Panel knobs are read as live values

**Status:** Accepted · 2026-09-17 · *user-directed* · reads a patch's own knobs
through [0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md)'s live block;
keeps [0021](0021-recompile-the-whole-patch-on-every-edit.md) for every edit but
a turn; stores links as [0061](0061-what-a-module-carries-is-kept-in-one-store.md)
state

## Context

A patch could only be performed by editing it. Turning an inspector knob writes
`InputValues`, records an undo step and recompiles the whole patch (0021), which
is fine for a hand on a slider and wrong for a MIDI controller sending a hundred
changes a second. The MIDI parser also threw every control change away. There was
nowhere to gather the handful of numbers somebody wants to reach during a set:
knobs lived in the inspector, one module at a time.

## Decision

**A patch has knobs.** `Patch.Controls` is a list of `PatchControl`: an id, a
name, a position from 0 to 1, and optionally the `MidiBinding` (device, channel,
controller) it follows. It is null when there are none, so older files and
readers are unaffected and `FormatVersion` does not move.

**A socket follows a knob, and a controller never names a socket.** A link is
`ControlLink(control, min, max)`, filed in the module's `State` under `controls`
by port index. One knob can drive any number of sockets, each over its own range,
and an inverted range turns it round. Learning a controller binds it to a knob, so
there is one path from a hand to a socket whether the hand is on the screen or on
hardware. Only a socket that would otherwise rest on its own knob can be linked:
a wire wins over a link, and a normalled or wire-only socket has no knob to take
over.

**Played, a link is a live value.** The three places the compiler falls back to a
socket's knob go through one helper. For a linked socket it emits
`LoadLive("control/{id}")` scaled to the range, and rounds for a stepped socket.
Turning a knob then writes one float into the running programs' blocks and
recompiles nothing. Links survive every edit and are re-seeded after each
recompile from where the knobs are.

**Not played, a link is baked in.** `CompileFor*` takes `played`, false by default.
Only the window's two live programs pass true. Everything else — CLI export,
checks and the assistant's renders — compiles each linked socket as a constant at
the knob's resting value. None of those renderers is handed a live block, and a
file rendered offline has nobody turning anything.

**A turn is not an edit.** Moving a knob, on screen or by a controller, records
nothing and does not mark the patch modified. Adding, removing, renaming, binding
and linking are edits. The shell's `ControlHub` keeps where each knob is and is
the authority across a recompile or an undo: where a performer's hand left a knob
is not something Ctrl+Z takes back.

**A controller that disagrees with a knob jumps or picks up.** The settings
window's MIDI tab chooses. *Jump* takes the controller at once. *Pick up* ignores
it until it reaches or passes the knob, and turning the knob on screen makes it
catch up again.

**Control changes are parsed.** `MidiMessages.Of` returns `MidiAction.Control`
with the controller number and value, and every message carries its channel.
`MidiHub` passes control changes on untouched and holds open whatever devices the
knobs are bound to, as well as the ones the programs read.

## Consequences

**The panel sits under the canvas** and is toggled with Ctrl+K. Clicking a knob's
name links sockets by clicking their rows on the canvas, which tints every socket
that can follow a knob; Escape ends it. A linked socket shows its value in the
highlight color, and its inspector row becomes the knob's name, its range and a
button to let it go. The first socket linked to a new knob turns the knob to where
the socket already was, so linking changes nothing heard or seen.

**Unlinking or removing a knob leaves each socket where the knob had put it.**

**Text does not know about knobs.** A text-owned patch carries them across an
evaluation for every module that keeps its identity (0067), but a `.fbks` file
does not write them, so they are lost when it is saved and reopened.

**A linked socket costs a live read and two arithmetic ops**, per program, in place
of a constant. Constant folding cannot reach through it.

## Amendment, 2026-09-23: a learned knob keeps its channel on a drum machine

A learn stored the controller on any channel, so a controller moved to another
channel went on turning the knob. A drum machine sends the same number from
every track and the channel is the whole of what tells them apart, so a knob
learned from an instrument whose profile has a track per channel
([0140](0140-an-instrument-is-known-by-a-profile-file.md)) keeps the channel the
controller moved on, and one learned from anything else stays on any channel.
The hub reports the channel either way; the window decides.

A learn can also walk the panel: the knob chosen and every knob after it in
turn, each waiting for its own controller, until the panel ends or Escape stops
it. The controller a knob was just learned from is not taken for the knob after,
because the hand that turned it is still on it when the next knob starts
waiting.
