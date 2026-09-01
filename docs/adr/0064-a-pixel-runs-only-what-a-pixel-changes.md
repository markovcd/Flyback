# ADR-0064: A pixel runs only what a pixel changes

**Status:** Accepted · 2026-09-01 · builds on
[0006](0006-scalar-interpreter-parallel-over-rows.md), whose finding that this
loop is dispatch-bound is what makes shortening it the whole of the win

## Context

[0006](0006-scalar-interpreter-parallel-over-rows.md) established that the
interpreter is bound by its own dispatch rather than by the arithmetic in it: a
frame costs roughly what the op count says it costs, and nothing else. It
concluded that a cost linear in the op count is what a larger patch spends, and
left that as the reason for a GPU backend.

There was a second reading of the same fact that had not been taken. If a frame
costs the op count, then the way to make a frame cheaper on the processor is to
run fewer ops — and most of the ops in a patch are not about a pixel at all.

Counting the shipped presets, compiled for the screen, by the furthest of
`LoadX`, `LoadY` and `LoadT` that reaches each op:

| Preset | Ops | Frame | Row | Pixel |
|---|---|---|---|---|
| Plasma | 33 | 14 | 6 | 13 |
| Kaleidoscope | 41 | 18 | 3 | 20 |
| Nebula | 80 | 26 | 6 | 48 |
| Feedback tunnel | 45 | 16 | 3 | 26 |
| Whole band | 598 | 526 | 9 | 63 |

A literal, a scale factor, an envelope on the clock, a sequencer stepping — none
of them can produce a different number for two pixels of one frame, and every
one of them was being re-derived half a million times a frame. The largest
preset in the catalogue spends eighty-nine per cent of its program that way.

The register file is single-assignment: the emitter hands out a fresh register
for every value and never reuses one. So which stage an op belongs to can be
read straight off the stages of the registers it reads, in one walk.

## Decision

`CompiledPatch` carries a `FramePlan`: the same ops, sorted into three stages by
what they vary with, with the two boundaries between them. `EvaluateStage` runs
one stage, and `SynthRenderer` runs the frame's and the row's shares once per
scanline and only the pixel's share per pixel.

A stage is the greatest of its inputs' stages, so a stable sort by stage never
moves an op ahead of something it reads, and the three runs compute exactly what
one run of the original did — the same doubles, not merely close ones.

Reordering is sound because the video path passes no `DelayState`. Without one,
every op in the instruction set is a pure function of its inputs: a delay hands
its input through, an accumulator is the multiply it replaces, a cell reads zero
and a tap does nothing. Which of them ran first stops being a question. On the
audio path the state exists and the question is real, so `Evaluate` keeps
walking the program in the order it was written, and the audio path is untouched.

The plan is refused, and null, for a program that writes a register twice —
which the compiler never emits, and which a program assembled by hand might.
A caller staging its loops still draws the right picture there: the pixel stage
runs the whole program and the other two do nothing.

Alongside it, the register file is now read without a bounds check. What pays
for that is a walk in the constructor that checks every index an op names
against the bank it will be given, using an arity table (`OpShape`) so that the
`-1` an op leaves in a field it does not read is not mistaken for a register out
of range.

## Consequences

Measured on an i5-14600KF, .NET 10, Release, BenchmarkDotNet 0.13.12, both arms
in one process so the ratio is not at the mercy of the machine's clocks. One
scanline of the renderer's inner loop — interpreter, clamp, frame history and
BGRA write:

| Preset | Whole | Staged | |
|---|---|---|---|
| Plasma | 61.9 µs | 38.0 µs | 1.63× |
| Feedback tunnel | 67.6 µs | 49.4 µs | 1.37× |
| Nebula | 152.2 µs | 124.8 µs | 1.22× |
| Whole band | 658.3 µs | 142.8 µs | 4.61× |

The interpreter alone, without the renderer's per-pixel tail, ranges from 1.79×
on Plasma to 5.39× on Whole band. The audio path, which only gets the unchecked
registers, comes out three to eight per cent ahead.

The saving is largest exactly where it was needed. A small patch was never the
problem; a big one was, and the bigger the patch the more of it turns out to be
about the frame rather than the pixel. That is not a coincidence — patches grow
by gaining voices, sequencers and envelopes, and those run on the clock.

The frame's share is redone once per row rather than once per frame. Each worker
in the `Parallel.For` has a register bank of its own and there is nowhere shared
to leave the result, and at a few dozen ops against a row of a thousand pixels
it is not a cost worth a handshake over. It does mean the frame stage is run as
many times as there are rows.

`CompiledPatch.Ops` is unchanged and still in emission order, so the GLSL
backend ([0035](0035-a-glsl-backend-for-the-video-path.md)) and everything else
reading the program sees what it always saw. The plan is a second view, not a
replacement.

The unchecked register access moves a class of malformed program from an
`IndexOutOfRangeException` somewhere inside the render loop to an
`ArgumentException` naming the instruction, thrown when the program is built.
That is a better failure, but it is a louder one: a hand-assembled program that
was quietly wrong now refuses to be constructed at all.

Something was measured and rejected. Skipping the float frame history for
patches with no Feedback module removes twelve of the sixteen bytes written per
pixel, and is worth about one and a half per cent — not enough to be worth
tracking whether the history a later patch reads is one this renderer actually
drew.
