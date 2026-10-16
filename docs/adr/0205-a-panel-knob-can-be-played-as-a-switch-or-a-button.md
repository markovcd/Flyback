# ADR-0205: A panel knob can be played as a switch or a button

**Status:** Accepted · 2026-10-16 · *user-directed* · extends
[0086](0086-panel-knobs-are-read-as-live-values.md) and
[0170](0170-the-knob-panel-randomizes-and-a-held-knob-sits-it-out.md)

## Context

A panel knob is a float from 0 to 1. Linked to a gate, a trigger or a `hold`,
it is a mushy switch: halfway reads 0.5, and a performer wants a sound or a
picture flipped between two settings, not swept. Panel knobs bound MIDI
controllers only; a controller's buttons and a drum machine's pads, the
natural hardware for on and off, reached nothing but the randomize trigger.

## Decision

**A switch and a button are kinds of panel knob, not a second panel.**
`PatchControl.Kind` is `Knob`, `Switch` or `Button`. Each keeps the 0..1 value,
the links and the live-value path of 0086; a switch or a button rests only at 0
or 1, snapped by the value itself. A socket linked to one reads the bottom of
its range when off and the top when on, so a switch over `cutoff(200..4000)` is
an A/B flip. Linked on the canvas, off reads where the socket rested and on the
far end of its range, so linking changes nothing heard.

**A switch flips and stays; a button is on only while held.** On screen a click
flips a switch and a press holds a button. Space and Enter do the same.

**Hardware.** Either binds a controller's button (`cc:`) or a pad or key
(`note:`, `MidiBinding.Note`). A pad flips a switch at each strike and holds a
button from note-on to note-off. A controller's button holds a button down at
64 and above. A switch on one flips at each press, unless the button is
`Latching`: it holds itself down, sending on at one press and off at the next,
and the switch follows it. Controllers ship either way and say nothing about
which, so a switch's learn takes the press at once and listens for the release:
none within 600 ms is a latching button. Neither kind uses jump or pick-up;
both take the controller at once.

**Randomize flips a switch, with the reach as its chance, and leaves a button
alone.** A button is played, not set, so a randomize landing one on would hold
it down with nobody's hand on it.

**The text says so:** `panel drop = 0, switch, cc: 64, device: "…", latching`,
`panel hit = 0, button, note: 36, …`. A switch or a button resting anywhere but
0 or 1, `note:` on a knob or beside `cc:`, and `latching` off a switch on a
`cc:` are refused.

## Consequences

- `Kind` and `Latching` are saved only when set, so a patch without either
  writes and reads as before and `FormatVersion` does not move.
- The contract gains `ControlKind`, `PatchControl.Kind` and
  `MidiBinding.Latching`.
- A button held when a patch is saved is saved on; it opens on until pressed.
- A momentary button held longer than the learn's window is learned as
  latching. Learning it again with a quick press, or deleting `latching` from
  its line, puts it right.
- The web viewer shows a switch or a button as a knob that flips or holds; it
  has no face of its own there.
