# ADR-0099: The computer keyboard can be laid out by scale

**Status:** Accepted · 2026-09-18 · *user-directed* · extends
[0062](0062-indexed-polyphonic-midi-voices.md) and reuses the scale of
[0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)

## Context

The computer keyboard is read as a tracker's piano: white notes on the bottom
row, black ones above, and the same an octave up. That is right for somebody who
knows where the notes are and wrong for somebody who wants to play in a key
without knowing it: half the keys are wrong notes, and which half moves with
the key.

A Quantiser already solves the same problem for a signal. Its scale is a set of
pitch classes on the node, picked on a drawn octave.

## Decision

**MIDI In gains a `keys` field, `piano` or `scale`, and carries a scale.** The
field is declared alongside `device` and `voice`; the scale is the same
`ScaleExtra` a Quantiser carries, so the inspector draws the same keyboard, the
text language writes it as the same block and the assistant's `set_scale` tool
sets it. It starts empty, so a MIDI In played as a piano prints as it did.

**In `scale`, a row is an octave of the picked notes, side by side.** The home
row (`A` to `\`) is where the piano's lower octave was, the `Q` row an octave
up and the `Z` row an octave down. A row holds as many notes as are picked and
the keys after them play nothing, rather than running on into the next octave:
the key under a finger is then always the same degree of the scale, and the
row above is always the same key an octave up. The `Z` row has ten keys, so a
scale of eleven or twelve loses its top notes there.

**The layout belongs to the keyboard, not the module.** There is one keyboard
and it can only be laid out one way, so the window asks the patch on every
recompile: the first MIDI In listening to the keyboard that asks for a scale
decides it, in patch order. A layout that changes lets go of what is held, for
the reason moving the octave does; one that stays the same leaves it alone.

**An empty scale plays nothing.** Switching to `scale` and picking nothing is a
choice, and falling back to the piano would make the keys mean something the
panel does not show.

## Consequences

- Modules listening to a hardware device ignore `keys`; a MIDI keyboard's notes
  are its own.
- Two modules asking for two scales do not both get one. The panel does not say
  which one won; that is a cost accepted for a case with no good answer.
