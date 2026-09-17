# ADR-0090: A take is counted in, and starts at zero

**Status:** Accepted · 2026-09-17 · *user-directed* · amends
[0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md)'s Record,
and puts [0081](0081-rewind-moves-to-the-toolbar-beside-record.md)'s Rewind
inside it

## Context

Pressing Record put the file picker up and opened the file the moment it was
answered. Two things followed from that, both of them a performer's problem
rather than a program's.

The first second of every take was spent letting go of the mouse. Naming a
file is a dialog, and whatever the patch was doing while the dialog was up is
the first thing in the file.

And a take began wherever the session had got to — five minutes of fiddling
in, with every envelope somewhere in the middle and every feedback buffer
full of what came before. Putting that right meant pressing Rewind first and
then Record, which is two presses in an order nothing said, and the picker
sat between them anyway: whatever the rewind had set up, the take started
some seconds after it.

## Decision

**Naming the file counts three seconds in on the status bar, then takes the
patch back to zero, then opens the file.** In that order, and the order is the
decision: a rewind before the count would be three seconds of a patch running
on from zero, and a rewind after the take had started would be a take of
whatever was on screen before it.

**`CountInAsync` is where all of that lives**, between the picker in
`RecordAsync` and `Start`. It reports one line a second — `Recording take.avi
in 3…` — as progress, so the count leaves the log a single entry the way a
running take already does, and says nothing to the terminal that `Said`
feeds: a number nobody reads afterwards is not a thing that happened.

**Three seconds, as a constant and not a setting.** Nothing was asked about
making it adjustable, and a count-in is a convention — the length every
sequencer counts a bar in at — rather than a preference. `CountIn` and
`CountInStep` sit with `RecordingTick` at the top of the region.

**The count reads the patch at its end, not at its start.** `Start` is handed
`editor.Patch` after the last number, because a count is three seconds
somebody may still be spending on the patch, and ADR-0021 recompiles on every
edit of it.

**The count is called off by the same button that started it, and the glyph is
the square throughout.** `counting` — a `CancellationTokenSource`, live only
while a count runs — joins `recorder` and `finishing` as a state
`MarkRecordable` reads and `ToggleRecordAsync` dispatches on, with
`CountingTip` for the tip. It is the one part of a take that can be called
off, since nothing has been opened yet: the name has only been chosen, and
`{name} was not recorded.` is the whole of what happened.

**A count-in keeps the button enabled whatever the patch reaches**, the same
exemption a running take already had in `MarkRecordable`: unwiring the Output
mid-count must not strand the count with no way to stop it.

**Why a take cannot be written is asked before the count as well as at the
file**, which is what the extracted `Refusal` is for — no ffmpeg on `PATH`,
or a sound file of a patch that is not playing. Three seconds of standing
ready and then being told there is no encoder is three seconds nobody gets
back; asking again as the file opens is not belt and braces but the honest
reading of a window that was live throughout the count, where the Volume may
have gone down inside it.

**Closing the window calls the count off** — `FinishTakeNow` and
`FinishTakeAsync` both — and a count is not a `TakeInHand`: there is no file
to finish, so the close is not held up for one.

## Consequences

**A take starts at nought seconds, every time**, which is what makes two takes
of one patch comparable at all, and what makes the first frame of a file the
first frame of the patch.

**Rewind is now two things**: the button ADR-0081 put on the toolbar, and the
first thing every take does. The button stays — putting the patch back without
recording it is what it is for — but nobody has to press it before Record any
more, and `RecordTip` says so.

**Three seconds pass between the press and the file.** A take of something
about to happen on its own — a sequencer arriving at a section — has to be
started three seconds early, and the numbers on the bar are what say how
early.

**`Start` is reached only through the count**, so its own refusals are now the
second asking rather than the first. `CountInAsync` is `internal` and takes
its step as a parameter, which is how a test counts a take in without waiting
out three real seconds.
