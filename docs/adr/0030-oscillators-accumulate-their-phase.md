# ADR-0030: Oscillators accumulate their phase on the audio path

**Status:** Accepted · 2026-08-16 · extends
[0027](0027-delay-lines-give-the-audio-path-a-memory.md), amends
[0006](0006-scalar-interpreter-parallel-over-rows.md)

## Context

Every oscillator computed its phase as `in × freq`. That is the natural shape for
a machine whose whole job is to be a function of `(x, y, t)`
([0005](0005-compile-to-a-flat-register-machine.md),
[0006](0006-scalar-interpreter-parallel-over-rows.md)): a picture has no previous
sample, its pixels are evaluated in parallel and in no fixed order, and a phase
that had to be carried from somewhere could not be drawn at all.

It is also wrong for a pitch that moves. Phase is the *integral* of frequency,
not its product with time, and the two only agree while `freq` is constant. When
`freq` steps from `f₁` to `f₂` at time `t₀`, the product jumps by `t₀ × (f₂ − f₁)`
— at ten seconds, a semitone at A3 is a jump of about 131 whole cycles, whose
fractional part is arbitrary. The waveform is cut and restarted at a random
point. That is a click, and it gets worse the longer the patch has run.

The Note module made this audible, because a quantiser's entire purpose is to
move the pitch in steps. Two fixes were tried on the module itself and both were
the wrong shape:

- **Ramping across the last part of each step.** With `θ = t × f(t)` the pitch
  actually produced is `dθ/dt = f + t × f′`. A ramp makes `f′` finite instead of
  infinite, but multiplies it by `t`, so on Chromatic at ten seconds a semitone
  crossed in 67 ms produced a swing of nearly 2 kHz on a 220 Hz tone. It replaced
  a click with a wobble, and a *narrow* ramp is worse than none — the same
  excursion, held for longer.
- **Widening the ramp until it is clean.** At that width it is a portamento. The
  module stops being a quantiser, which is what it was for.

Neither could ever have worked, because neither addressed `t`.

## Decision

A third stateful opcode, `Phase`, alongside the two
[0027](0027-delay-lines-give-the-audio-path-a-memory.md) introduced. Every
oscillator emits one in place of its multiply.

**It accumulates how far its input moved, not how long the clock ran.**
`phase += (in − in_previous) × freq`, wrapped into `[0, 1)`, with the phase
offset added afterwards rather than integrated. A frequency that jumps therefore
moves the phase by one ordinary step, whatever it jumped by: the waveform's value
is continuous across the change and only its slope differs. A slope has no click
in it.

**Measuring the input rather than counting samples is what keeps `in` a socket.**
The obvious form is `phase += freq / sampleRate`, which is what a conventional
synth does — but it makes an oscillator a function of a clock, and here `in` is a
domain a patch may drive with any signal it likes. Taking the difference means a
domain that stops stops the phase, one that runs backwards runs it backwards, and
one that runs at twice the rate doubles the pitch, all of which the multiply also
did. It also means the op needs no sample rate at all.

**The offset is added outside the accumulator.** Integrating it would turn a
phase input into a frequency input, and a patch modulating one would hear the
other.

**Without state it is exactly `a × b + c`** — the multiply it replaces, bit for
bit. That is what the video path gets, and the approved frames from before this
change still verify unchanged.

**The first evaluation of a cell takes no step.** There is no previous input to
measure against, and starting from a guessed one would be a click of precisely
the kind this exists to remove.

## Consequences

**The Note module is a plain `floor(note + 0.5)` again**, and its `glide` input
is gone. Nothing about the pitch is smoothed, because there is nothing left to
smooth. Measured on Chromatic as the largest sample-to-sample jump against the
median one — a tear being a step far larger than the wave's own travel — over one
second of raw program output at 192 kHz:

| measured from | multiplied | accumulated |
|---|---|---|
| 0 s | 237× | 1.6× |
| 20 s | 538× | 1.8× |
| 60 s | 648× | 2.6× |

**An `in` left on its knob is now worth a warning, and did not used to be.**
Under the multiply, `in × freq` with `in` constant was a fixed phase — wrong, but
the same kind of wrong as any other constant. Under the accumulator it is
structural: the phase moves by `(in − in_before) × freq`, so an `in` that never
changes moves it by nothing at all, whatever `freq` says. A sine wired to a
speaker with its `freq` correct and its `in` on the knob is silence, and reads
like a working patch. `PortSpec.Domain` marks the socket and the compiler says so
([0011](0011-compile-backwards-from-output.md)) — a `Warning`, because a still
picture is a real thing to want, and because the patch it describes is exactly
the one that was built. It is the first compile issue about a patch that is
valid, which is what the severity is for.

**Almost every audio patch now needs state, where before only a delay did.**
`DelayMemoryFor` hands memory to any program with an accumulator, so a plain
sine and a speaker allocates a `DelayState` — three small arrays, no buffers.
Chromatic declares two cells and no lines.

**A recompile that changes the accumulators resets every phase.** Same rule as a
delay's buffers and the same reason: cells are identified by position among the
ops. Turning a knob keeps the oscillators running; adding or deleting one restarts
them all, which is a click on an edit rather than during playback.

**The drift at 60 s in the table above is `t` being a `float`, not the
accumulator.** Past about a minute the spacing of representable floats near `t`
approaches the oversampled sample interval, so the measured step jitters by a
fraction of itself. That shows up as a little noise, never as a tear, and it is a
property `Evaluate`'s `float t` already had — the multiply's phase was far
coarser at the same point. Fixing it means a wider time input, which is a
separate decision.

**The asymmetry [0027](0027-delay-lines-give-the-audio-path-a-memory.md) opened
against [0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) now touches
the oscillators**, which are the most-used modules in the catalogue. It is a
gentler asymmetry than the delay's: a Delay is a wire in the picture and does
something else entirely in the sound, while an oscillator draws exactly what it
always drew and only differs where a difference cannot be seen. Nothing in the
palette says so, because for a patch that is not changing an oscillator's `freq`
there is nothing to say.

What this does not do: the waveforms are still naive, so `Saw` and `Square` alias
on their own edges and lean on [0023](0023-oversample-the-audio-path.md) rather
than on band-limiting; that discontinuity is in the wave's shape and is a
different problem from this one. There is no portamento module to replace the
`glide` that went, and a musical slide is now something the catalogue cannot do.
