# ADR-0126: A bus is a wire with no cable

**Status:** Accepted · 2026-09-21 · *user-directed* · follows
[0125](0125-a-duck-is-a-sidechain-with-the-depth-on-a-knob.md) and
[0075](0075-a-cycle-carries-its-own-delay.md)

## Context

A kick keys everything it makes room for: the bass, the pad and the chords, in
a band laid out left to right with the drums at one end. Every part it ducks is
another wire across the whole canvas, and boxes only move where the wire
crosses. The same holds for a clock, a key or an envelope that half the patch
reads.

## Decision

**Send and Receive, two Maths modules on a named bus.** Whatever is patched
into a Send comes out of every Receive on the same bus.
- A Send's `out` is its `in`, so it can sit inside a chain.
- The bus is a text field, not the module's name. A name is a label, and in
  the text language it is the `let` a module was bound to, so two ends sharing
  one would be one name bound twice.
- Buses match regardless of case and surrounding spaces. A fresh Send and a
  fresh Receive are on the same bus, so the first pair needs no typing.

**Joined before anything is compiled.** `Buses.Joined` hands the compiler a
patch in which each wire out of a Receive comes from whatever feeds its Send.
From there a bus is a wire:
- Cycles cut a loop through one, and it carries the evaluation before, as any
  loop does ([0075](0075-a-cycle-carries-its-own-delay.md)).
- A module switched off along it is followed as usual.
- It costs nothing at run time.
- The saved patch keeps both ends, and nothing else in the engine knows about
  buses.

**A bus with no Send, or two, is said.** A Receive with no Send carries
nothing: the socket it feeds rests on its knob, as with the wire pulled out,
and the compiler says so. Of two Sends on one bus, the one with the lower id is
heard, because list order moves whenever the canvas brings a module to the
front, and the other is reported.

**The canvas shows the bus, not a wire.** The header reads `Send · kick`.
While one end is selected, a straight dotted line joins it to the others, so
it cannot be mistaken for a wire, which is curved and always drawn.

## Consequences

- A sidechain across a big patch is a Send by the kick and a Receive by each
  Duck.
- The canvas asks `Cycles` about the patch as drawn, so a loop closed through a
  bus is delayed but no wire on it is dashed.
