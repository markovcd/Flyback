# ADR-0130: An Auto remap reads its ranges off its wires

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0014](0014-coordinate-and-value-conventions.md) for what the ranges are, and
on the knee a socket's knob sweeps by

## Context

About seventy Remaps in the presets, and nearly every one starts from 0..1 or
−1..1: the range of the Sine, envelope or gate feeding it. Half of each Remap
restates what its source already is, and the other half is a number in the
destination's units that has to be looked up. `0.92..0.996` on a feedback
`keep` or `90..320` Hz on a cutoff says nothing about where on that socket's
knob the sweep sits.

The sockets already say most of this. An input's `Min` and `Max` are its knob's
range, and a frequency's knee sweeps it in decades. An output's `Min` and
`Max` were unused.

## Decision

**A new Maths module, Auto remap, with Remap's five sockets.** Its four range
knobs are fractions, 0 to 1:

- **`in low`/`in high`** are fractions of the range of the output feeding it.
- **`out low`/`out high`** are fractions of the range of the sockets it feeds,
  swept through that socket's knee the way its own knob sweeps.

The input is first read as how far along its source's range it is, the slice
is taken in travel, and the result is turned into the destination's range by
the socket's taper. So a sweep into a cutoff moves in octaves all along, not
only at its two ends. A color bound only for sockets that take one number is turned into
its brightness first, so pure red into a frequency lands a fifth of the way
round the knob rather than at an average of three sweeps.

**A socket has a range unless its `Min`/`Max` are the −4..4 placeholder,**
and a color socket is 0..1 on every channel, which is what the screen shows.
Waves, envelopes, gates, noise and the Voice plugin's sources now declare
their outputs. A source with `amp` and `bias` knobs is −1..1 before them, so
the range moves with those two knobs, and a wire on either leaves it unknown.

**No range means plain numbers, and Flyback says so.** A side whose far end has
no range (an Expression, an Add, a Value), or whose output feeds two sockets
with different ranges, takes its pair as plain numbers, as Remap does. The
compiler warns, which puts the reason in the status bar. The editor outlines
that pair in the accent color, and shows every fraction as the value it comes
to. An output feeding one socket with a range and one without follows the one
with. An unwired side is fractions of 0..1, which are the numbers themselves.

**A wire swinging past its socket's range is pointed out.** Where what a
source puts out reaches below or above the range its socket takes, the
compiler warns and the canvas draws the wire in the accent color, with its
remap mark lit. A socket marked `Lenient` is left alone, since a value past
its range still means something there: a phase or a hue wraps round, a gate or
a trigger reads a threshold. So is a color, which the screen clamps. The Drone,
which let the lower half of its rings fall off the bottom of `value` as black,
now fits them through an Auto remap, and the tutorial that builds it teaches
the orange wire.

**Nothing is saved but the knobs.** The ranges are worked out before each
compile by a pass beside the one that joins buses, and handed to the module on
its `EmitContext`. Rewiring changes what the fractions mean. It does not
rewrite the numbers.

## Consequences

Setting a sweep takes no knowledge of either end's units. "The lower third of
the cutoff" stays that when the Auto remap is rewired to another filter.

A patch's meaning now depends on its wiring in one more place. The outline and
the resolved readings are there so that nobody has to work it out.

The knobs are fractions only on a scale of 0..1. A −1..1 scale for ranges
centered on zero was considered and left for later.

Remap stays. Its numbers are what the Auto remap falls back to, and what
seventy presets are written in.
