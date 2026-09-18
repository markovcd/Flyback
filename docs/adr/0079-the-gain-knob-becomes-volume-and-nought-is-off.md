# ADR-0079: The gain knob becomes Volume, and nought is off

**Status:** Accepted · 2026-09-16 · *user-directed* · renames the Output's
third audio socket introduced by
[0037](0037-one-output-block-that-every-patch-has.md), which is amended for it
· removes the shell's manual audio toggle

## Context

The Output has carried a `gain` socket since
[0037](0037-one-output-block-that-every-patch-has.md) folded the two old sinks
into one block: a knob from 0 to 1, multiplied into `left` and `right` before
they reach the speakers. Beside it, on the toolbar and later the Output's own
panel, sat a second and unrelated control: a manual "Audio on/off" toggle that
opened and closed the actual device, switched the preview's clock between the
audio cursor and its own, and told every Meter to read silence.

The two never had to agree. `gain` at nought with the toggle still on left the
device open and the callback running for a signal that was already zero —
correct, just running a real-time audio thread and holding a piece of hardware
open to produce nothing. The toggle off with `gain` sitting wherever it was
left a number on the panel that meant nothing until the toggle was clicked
again. Two controls, answering what turned out to be one question: is this
patch meant to be heard right now.

`gain` is also a mixing term for a level relative to some reference, not for
the on/off a person reaches for when they turn a knob all the way down. A
volume knob at zero is a familiar, unambiguous gesture on ordinary audio
equipment — nobody reads it as "very quiet."

## Decision

**The socket is `volume`, not `gain`.** `NodeCatalog.OutputVolumePort` (was
`OutputGainPort`); `Num("volume", 0.5f, 0f, 1f)` in
`NodeCatalog.Output.cs`. The text language reads and writes `out.volume`
wherever it read and wrote `out.gain`, since the printer and the binder both
resolve the name off the port definition rather than a literal string —
nothing else in either changed.

**The toggle is gone, and Volume is what it was.** `MainWindow` no longer
carries an `audioButton`. `SetAudioEnabled` — still what opens and closes the
device, switches the preview's clock, and deafens the Meters — is no longer a
click handler. It is called by a new `SyncAudioToVolume`, itself called at the
end of every `Recompile` (ADR-0021 recompiles the whole patch on every edit,
so a slider drag calls this once a frame):

- **Volume wired:** on. There is no default left to read once something is
  patched in, and a signal driving it is presumably meant to be heard.
- **Volume unwired and above nought:** on.
- **Volume unwired and at or below nought:** off.

This is exactly the transition a click used to make, just made by the knob
that already meant the same thing mathematically. `SyncAudioToVolume` only
calls `SetAudioEnabled` when the wanted state differs from
`AudioEngine.IsRunning`, so the device is opened and closed on the crossing
and not re-touched on every other frame of a drag or edit.

**A device that refuses to open stays refused for the session.** The old
button went permanently disabled on a failed `Start()` — a card that is busy,
unplugged, or missing its library will not have fixed itself by the next
click. A new `audioBlocked` field does the same job: once set, `Sync-
AudioToVolume` wants audio off regardless of what Volume says, so a failure
is reported once rather than on every recompile a slider drag produces.

**No backend still means no audio, whatever Volume says.** `SilentAudioDevice`
reports `IsRunning` honestly and produces no callback ticks, so using it as
the preview's clock would freeze the picture — its own remarks say the shell
must disable sound outright when this is what it got. That guard is
unconditional in `SyncAudioToVolume` (`sound.Output is not null`) rather than
becoming a knob's job to know about. The one thing the toggle told the user
proactively — hovering a disabled button to read "no sound backend is
installed" — has no control left to hover, so the shell says it once at
startup instead, the same information `SetAudioEnabled`'s failure path
already gave for the "found a device but it would not open" case.

**`SetAudioEnabled` no longer calls `audio.Update` on the way in.** It has
exactly one caller now — `SyncAudioToVolume`, called from inside `Recompile`
after `Recompile` has already updated the engine with the current patch and
its sample library. A second update here, as the old click handler did,
would have overwritten that with one built without the sample library, which
was harmless only because a manual click never landed mid-`Recompile`.

## Consequences

**A `.fbks` file that still says `out.gain = …` no longer binds.** `gain` is
not a socket name any more, and nothing rewrites old text automatically — the
same trade [0077](0077-the-picture-is-heard-only-through-a-scan.md) made for
`scan`. A `.fbk` or `.fbkb` file is unaffected: `NodeInstance.InputValues` is
a plain array in port order, serialized positionally rather than by name, so
the rename touches nothing on disk for either binary format.

**Turning Volume down while a patch plays now stops the device outright,
where it used to keep running at a computed zero.** This is the entire point,
but it means a patch relying on the audio thread staying alive at silence —
none do, today — would need rewiring. The Scope and Analyzer still show
nothing while Volume is at nought, for the reason they always did: both are a
record of what the speakers actually played, and nothing did.

**The Output's own panel lost its last standalone control under "Sound."**
Rewind is what remains there; Volume itself is already visible above, as one
of the Output's ordinary knob rows, so nothing about the instrument became
harder to find — the switch moved onto the knob it always agreed with.

## Amendments

**2026-09-18 — nought is not off in the middle of a take, and a Volume that
follows a knob counts as wired.** Two cases where the stored number is the wrong
thing to ask. A take with sound in it is paced by the samples it is handed
([0049](0049-record-the-gpu-frame-not-the-interpreter.md)), so a device
stopped under it stops the file, picture included, at that instant — and fading
Volume to nought is how a take is ended. The device is left running for the
length of a take, which records the silence, and is asked about again when the
take is over. And a socket that follows a panel knob
([0086](0086-panel-knobs-are-read-as-live-values.md)) keeps its resting number
while it plays the knob's, with no recompile as the knob turns and so nothing to
ask this again as it crosses nought: resting at nought under a fader, the device
would never open. It is treated as a wire is, and for the reason a wire is —
something is driving Volume, and is presumably meant to be heard.
