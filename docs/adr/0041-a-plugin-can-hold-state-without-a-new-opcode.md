# ADR-0041: A plugin can hold state without a new opcode

**Status:** Accepted · 2026-08-19 · *user-directed* · amends
[0027](0027-delay-lines-give-the-audio-path-a-memory.md)

## Context

The catalogue had five oscillators and no filter. That is the one omission a
synthesiser cannot carry: the oscillators are naive saws and squares, kept clean
by oversampling ([0023](0023-oversample-the-audio-path.md)) and by nothing else,
and there was no module anywhere that changed the spectrum of a signal. Sweeping
a cutoff is the gesture the whole of subtractive synthesis is built on, and the
machine could not do it.

[0027](0027-delay-lines-give-the-audio-path-a-memory.md) said plainly why not:
*"No plugin can introduce state, however much it is asked to."* A plugin
contributes a `NodeDef` and an emit function over existing opcodes, and the
interpreter is a switch in `Flyback.Core`. A delay and a reverb therefore
arrived as two new opcodes.

That statement stopped being true, quietly and without anyone deciding it, when
the Unit Delay arrived. Letting a patch draw a cycle by hand — the thing
[0012](0012-feedback-as-a-module-not-a-cycle.md) had ruled out for the picture and
which the audio path can afford — meant a cell that carries a value from one
evaluation to the next, and gave the emitter `AllocateUnitSlot`, `UnitRead` and
`UnitWrite`. All three are public. The cells are counted off the ops that name
them, so nothing needs to know who asked. Nothing about any of it is reserved
for the cycle breaker that motivated it, and a module that wants a memory of
exactly one evaluation can simply take one.

## Decision

**The filter is a plugin, and it introduces no opcode.** Its two integrators are
two cells from `Emitter.AllocateUnitSlot`, read at the top of the emit function
and written at the bottom. The topology is Zavalishin's TPT state-variable
filter, which resolves its own implicit loop algebraically instead of iterating
it — so it is stable at every cutoff, including the ones a sweep only passes
through, and all three responses fall out of the same pair of integrators rather
than out of three filters behind a switch.

**A module works out its own sample rate.** Nothing tells one what rate it runs
at, and adding a way to would be a new kind of thing for an emit function to
know. It does not need one: a third cell holds the clock as it was last
evaluation, and the difference is the interval. This is
[0030](0030-oscillators-accumulate-their-phase.md)'s trick written out by hand —
an oscillator advances its phase by how far its domain moved, and this measures
how far `t` moved — and it means the filter is correct at 192 kHz, at the 48 kHz
a test drives it at, and at whatever a future path runs at, without being told.

**What a stateful module does with no state is a decision it makes, not one it
inherits.** A fourth cell is written `1` and read back as `1` from the second
evaluation onward — and read as `0` for ever where the renderer passes no state
at all. That flag is the only honest way to ask the question: an emit function
runs once, at compile time, long before anything knows which sink is about to
run the program. Mixing on it gives the video path a chosen answer instead of an
emergent one.

**The answer chosen is the filter's response to a signal that never moves.** A
picture is one evaluation per pixel with nothing before it, so a filter sees DC —
and at DC a lowpass passes everything, a highpass and a bandpass pass nothing.
The picture a patch drew before a Filter was put in it is the picture it draws
after.

**The folder and the saturator beside it are pure and shared.** Neither has any
state, so neither needs any of the above: the same arithmetic runs at both sinks,
and the harmonics the ear hears from a fold are the bands the eye sees in a
gradient, from one knob. Their ports are `Any`, so they shape a colour the way a
Multiply does.

## Consequences

**A whole class of module is now a plugin rather than an engine change.** Anything
whose memory is one evaluation deep — a filter, a slew limiter, a sample and
hold, a one-pole smoother — can be written outside `Flyback.Core` by anyone with
the two host-owned assemblies. What still cannot: anything needing a *buffer*,
because a ring of thousands of samples has no cell to live in and `Delay` and
`Allpass` remain opcodes for that reason.

**`Evaluate` gained no case and the GLSL backend gained no work.** The video path
already knows what a `UnitRead` with no state means, because the Unit Delay
taught it.

**The video fallback is deliberate where the Reverb's is emergent.**
[0027](0027-delay-lines-give-the-audio-path-a-memory.md) records the Reverb
dimming the picture by one minus its feedback, and records it as a price rather
than a choice. A flag costs three ops and buys the choice back. That the Reverb
could be rewritten this way is true and is not being claimed as done here.

**Nothing pins the cost of the flag to the filter.** Any module reaching for the
same trick pays another two cells and gets the same answer, and four modules
doing it would ask the renderer for eight cells that all hold the same number.
This is cheap enough not to matter and repetitive enough to be worth noticing if
a third module wants it.

**Prewarping puts the corner where the knob says and does not straighten the
rest of the curve.** A decade above the cutoff the response is a little under the
hundredth an analogue prototype would give, because a sampled filter bends
towards Nyquist. The tests pin the measured value rather than the ideal one.
