# ADR-0203: A Plot draws a pitch function as a curve in the picture

**Status:** Accepted · 2026-10-15 · *user-directed* · reuses the sweep
[0040](0040-a-probe-is-a-second-compile-root.md) made for the Probe

## Context

A song written as functions of beats is shown by drawing each function as a curve
with a dot riding it at now. Flyback could already draw that, but only by hand: the
famous-functions song spends thirty lines of its picture on mapping pitch to
height, a page of eight beats, a vertical stroke at each jump and a dot, and the
next song would copy them. The Probe already draws a function ahead of time by
sweeping its input, but it charts seconds, rules now down the middle, has one
color and replaces the picture rather than being part of it.

## Decision

**Plot is a module of the Picture plugin, not of the engine.** It is drawn into
the patch's own picture, so it needs nothing the shell gives a chart, and the
sweep it uses (`PortSpec.Swept`, `Emitter.PushDomain`) is already in the plugin
contract. The contract does not grow.

**'in' is a pitch, read across beats.** 1 is an octave, matching
`55 Hz × 2^y`. The beats count at 'bpm' from time 0, and the chart is 'span'
beats wide inside a box set by 'left', 'right', 'bottom' and 'top', with 'low'
and 'high' the pitches at its edges.

**Two ways to move.** 'page' 0 scrolls the curve past a fixed now, 'ahead' of
the span still to come; 'page' 1 holds 'span' beats still and moves now across
them. Either way what is still to come is drawn at 'future' brightness.

**Three evaluations of 'in'.** One at the column's beat, one a small step before
it so a jump is drawn as a vertical stroke, and one at now for the dot. Each is
a sweep, so a function that reads only time costs three times its ops in the
picture and nothing in the sound.

**Curves stack through 'under'.** 'out' is 'under' with the curve added, so a song
of five functions is five Plots in a chain into the Output's 'color'. 'ink' is
the curve alone, for a Layer or a Mixer.

## Consequences

- A song made of functions draws its curves without pixel arithmetic, and the
  picture stays ordinary ops, so it survives to the shader and exports headless.
- What the Probe cannot show, a Plot cannot either: an oscillator's phase, a
  delay or a triggered envelope is recomputed rather than remembered.
- The three-dimensional road some songs use is not a mode. It is a Transform
  of the chart's coordinates if anybody wants it.
