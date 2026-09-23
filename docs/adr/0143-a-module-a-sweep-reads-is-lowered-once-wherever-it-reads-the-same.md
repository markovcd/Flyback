# ADR-0143: A module a sweep reads is lowered once wherever it reads the same

**Status:** Accepted · 2026-09-23 · amends
[0040](0040-a-probe-is-a-second-compile-root.md)

## Context

A swept input is lowered under a domain the module pushes
([0040](0040-a-probe-is-a-second-compile-root.md)), and the compiler gave each
sweep a cache of its own, so everything upstream was lowered again every time.
For a Probe that is one extra copy. Overtones sweeps once per partial, and Sand
read a plate through it: eight copies of the plate, each with its own strike in
three planes of its own, and eight copies of the pulse and the sines striking
it. That was 1,064 of the sound's 1,840 ops, and the sound ran under real time.

Almost none of it depended on the place being read. The pulse reads the clock,
which Overtones leaves alone; the plate's ring reads no pixel at all. Only where
the pixel sits on the plate does.

## Decision

**The compiler remembers what each node was lowered to and which of x, y and t
it read**, counting what everything upstream of it read. A node is lowered
again only where a pushed domain replaced one of those. A knob, or a sine
reading the clock under a sweep that keeps the clock, is one register for all
of them. The per-sweep cache is gone: under one domain the memo is that cache.

**`Emitter.Once` does the same for part of a module.** A module hands it a key,
the slots it reads and a function that lowers the part; the emitter keeps the
answer for that module and key, and hands it back wherever the slots and the
x, y and t it read are the same. State it claims is claimed once, so two
readings of one plate are one plate struck once. Plate puts its strike and its
modes' ring behind one, and its sines across and down behind one each, so the
row Overtones reads is one set of sines for every partial.

Both are exact. A value that reads nothing a push replaced is the same value
under it, and memory that is handed the same inputs holds the same thing.
Sand's sound and picture come out bit-identical to before.

## Consequences

- Sand's sound is 1,093 ops and eleven planes rather than 1,840 and
  thirty-two, and runs about twice as fast; its picture is 958 ops a pixel
  rather than 1,705.
- A module lowered under a sweep keeps state of its own only where its value
  depends on the place. A Probe charting something upstream of a knob-only
  branch shares that branch with the picture rather than copying it.
- Which of x, y and t a lowering read is noticed, but every other slot a
  `Once` part reads has to be named in its inputs: the emitter cannot see what
  the function closes over.
