# ADR-0170: The knob panel randomizes, and a held knob sits it out

**Status:** Accepted · 2026-10-01 · *user-directed* · builds on
[0086](0086-panel-knobs-are-read-as-live-values.md)'s live knobs; implemented in
`src/Flyback.Editor/Knobs/KnobRandomizer.cs`

## Context

The panel is where a patch is performed, and the user wanted it to be where one is
explored too: send the knobs somewhere new, listen and look, keep it or go back. As a
performance move it has to be reachable from the keyboard and from hardware, land
without a click, and leave alone the knobs a set depends on.

## Decision

**A randomize turns knobs the way a hand does.** Each knob not held goes somewhere
within the amount of where it is, evenly, an amount of 1 being anywhere on it, and
glides there eased in and out. Every step is `PanelKnobs.Turn`, so it writes live
values and recompiles nothing, and a hand or a controller that takes a gliding knob
takes it from the glide.

**It is not an undo step.** Turning a knob never was (0086): the hub keeps where a
hand left a knob over what an undo restores. So a randomize has its own way back,
a stack of where every knob was before each one, which the panel's arrow glides back
through.

**Held is the patch's; amount, glide and the trigger are the performer's.**
`PatchControl.Held` is saved with the patch and written in its text as a bare `held`
after a panel line's settings, since which knobs a patch can survive moving is about
the patch. How far, how slowly and which controller button fires it are about the
hands and the hardware, so they live in `OutputSettings.Randomize` and are set on the
panel itself. The trigger is a CC, firing on the press, or a note struck on a
pad, learned from whichever arrives first. A note is heard by the trigger as well
as played, never instead, so it is not taken from a MIDI In listening to it.

## Consequences

- `PatchControl.Held` moves the plugin contract to 1.1.0.
- Ctrl+Shift+K randomizes even while the picture has the window.
- `MidiBinding.Note` says a binding is a note; a knob still only learns a CC.
