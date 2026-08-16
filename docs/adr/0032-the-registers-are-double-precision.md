# ADR-0032: The registers are double precision

**Status:** Accepted · 2026-08-16 · settles the open question in
[0030](0030-oscillators-accumulate-their-phase.md), amends
[0005](0005-compile-to-a-flat-register-machine.md) and
[0006](0006-scalar-interpreter-parallel-over-rows.md)

## Context

[0030](0030-oscillators-accumulate-their-phase.md) closed by naming a problem it
had chosen not to solve: *"the drift at 60 s in the table above is `t` being a
`float`, not the accumulator… Fixing it means a wider time input, which is a
separate decision."* This is that decision, and the reason it stopped being
deferrable is that someone left the Sequence preset playing and heard it.

**A ringing tone appears after about a quarter of an hour and gets louder.** The
mechanism is entirely in the width of the time input. The audio path evaluates
the program at 192 kHz ([0023](0023-oversample-the-audio-path.md)), so
consecutive evaluations are 5.21 µs apart, while the spacing of representable
`float`s near `t` doubles every time `t` does:

| `t` | float spacing | evaluations sharing one value | staircase rate |
|---|---|---|---|
| 30 s | 1.9 µs | — | — |
| 60 s | 3.8 µs | — | — |
| 250 s | 15.3 µs | 2.9 | 65 kHz |
| 1 000 s | 61.0 µs | 11.7 | 16.4 kHz |
| 1 500 s | 122.1 µs | 23.4 | 8.2 kHz |

Past 64 seconds two consecutive sample times can be the *same float*. `t` stops
being a ramp and becomes a staircase, and an oscillator that measures how far its
input moved ([0030](0030-oscillators-accumulate-their-phase.md)) is handed a
sequence of eleven zero steps and one large one. That is a sample-and-hold, and
its rate is the reciprocal of the float spacing — which walks *down* through the
audible band as the session runs, straight past the decimation filter's 21.6 kHz
cutoff and on into the middle of the top octave.

Measured on Sequence, as the loudest bin above 10 kHz over a 16 k FFT of the
program's own output:

| measured from | peak above 10 kHz |
|---|---|
| 60 s | 0.25 |
| 300 s | 0.38 |
| 1 000 s | 7.61, at 16.2 kHz |
| 1 500 s | 13.41 |

The 16.2 kHz is 16 384 Hz minus the tone that is playing: the staircase rate,
with the sequence reflected about it. It is not the oscillator, not the
sequencer, and not the decimator. It is the clock.

Two fixes were considered and are worth recording, because both look plausible
and neither works:

- **Wrap `t` on the audio path so it never grows.** Every consumer of `t` jumps
  at the wrap — the sequencer restarts mid-pattern, an accumulator takes one huge
  step — so it trades a ring for a click. Choosing a wrap period that is a whole
  number of steps for one patch does not make it one for the next.
- **Count samples instead: `phase += freq / rate`.** This is what a conventional
  synth does, and [0030](0030-oscillators-accumulate-their-phase.md) rejected it
  on purpose: `in` is a socket, and an oscillator driven by a clock cannot be
  driven by a signal.

Neither addresses the width, which is the only thing wrong.

## Decision

**The register file is `double`.** `Evaluate` takes `double x, y, t` and a
`Span<double>`; every op, guard and helper widens with it. The audio renderer
passes its cursor through unnarrowed, which is the whole point — it always held
the time in `double` and threw the low bits away at the call.

**It is not free precision for its own sake; it is precision where the domain
lives.** Nothing a sink emits needs 53 bits: a pixel is eight and a sample is
sixteen. What needs them is `t`, because `t` counts up without bound while the
interval between evaluations stays fixed, and no other quantity in the machine
does that.

**Two things stay `float`, for that reason.** The delay lines hold a signal on
its way to a speaker, and the frame history holds a picture
([0012](0012-feedback-as-a-module-not-a-cycle.md)); both are values, not
positions on a clock, and doubling them would double the largest allocations in
the program to buy nothing. The registers around them are wider than they are,
which is the correct asymmetry rather than an oversight.

**The video path is widened too, rather than kept apart.** A machine with two
register widths means either a generic interpreter or two copies of it, and this
one is dispatch-bound: measured over each preset at 960×540, `double` came in
between 0.88× and 1.09× of `float`, which is noise. Paying nothing for one
interpreter is better than paying nothing for two.

## Consequences

**The ringing is gone, and so is the noise that preceded it.** The same
measurement now reads 0.000 above 10 kHz at 0, 10, 60, 300, 1 000 and 1 500
seconds — including the 60 s and 300 s figures above, which were the same defect
too quiet to name. The spacing of a `double` near 1 000 s is 0.1 ps, about eight
orders of magnitude finer than the sample interval, so there is no `t` a session
can reach where this returns.

**Seven approved frames were re-approved.** Nothing moved: at most 36 pixels of
57 600 changed, every one of them by one part in 255, which is rounding at the
byte boundary and nowhere else. [0030](0030-oscillators-accumulate-their-phase.md)
could say the frames verified unchanged because it left the arithmetic alone;
this changes the arithmetic, and one bit of one byte is what that costs.

**`exp(200)` is now a number.** [0013](0013-guard-arithmetic-instead-of-propagating-nan.md)'s
guards fire on what is *not finite*, and where a float overflows past about 88 a
double runs to about 710 — so the overflow example in the guarded-arithmetic spec
had to move to 1 000 to still be one. The rule did not change; the edge it is
stated at belongs to the register width, and that is now recorded in the spec.

**Nothing in the catalogue or the file format is touched.** Knob values, port
defaults and `Op.K` are still `float`, and a saved patch is byte-for-byte what it
was ([0020](0020-json-patch-files-keyed-by-string-type-ids.md)). The width is a
property of evaluation, not of the patch.

**The register banks double, and they were never the memory that mattered.** A
row's scratch is one array of `RegisterCount` — Nebula's 78, Sequence's 109 —
against a frame history of `width × height × 3` floats that has not changed.

What this does not do: the waveforms are still naive
([0030](0030-oscillators-accumulate-their-phase.md)), `Saw` and `Square` still
alias on their own edges, and a patch that was clicking for some other reason is
clicking still. This fixes one thing — a clock that could not count — and the
reason it was worth its own record is that the symptom was audible only after
twenty minutes, which is longer than anyone tests for.
