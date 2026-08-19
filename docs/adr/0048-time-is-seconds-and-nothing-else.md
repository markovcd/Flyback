# ADR-0048: Time is seconds, and nothing else

**Status:** Accepted · 2026-08-20 · *user-directed* · takes a socket off a module
[0008](0008-modules-as-data-in-one-catalogue.md) holds as data, and removes the
second reading of [0030](0030-oscillators-accumulate-their-phase.md)'s domain

## Context

The **Time** module had one input, `rate`, and emitted `t × rate`. It was there
so a picture could be slowed down without a Multiply in front of every consumer,
and for a picture it works: most presets ran their clocks somewhere between 0.05
and 0.3.

It is a different thing on the audio path, and that only became clear once an
agent was building patches ([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)).
It set `rate` low as a matter of habit — reasonably, having been told the knob
scales time — and the patches came out an octave or three flat.

The mechanism is [0030](0030-oscillators-accumulate-their-phase.md). An
oscillator accumulates how far its `in` has moved, so a domain advancing at a
fifth of a second per second is a pitch divided by five. A 440 Hz oscillator fed
a Time at 0.2 is an 88 Hz oscillator whose own knob still says 440. Nothing in
the patch shows where the fifth went: the knob is right, the wire is right, and
the tone is wrong.

Three things made it worse than an ordinary mistake:

- **It is silent.** The compiler has nothing to complain about, the picture is
  unaffected in any way that reads as an error, and the only symptom is a pitch
  nobody can check against anything.
- **It contradicts the briefing.** The conventions given to an agent say plainly
  that `t` is seconds since the patch started. With a rate knob, that was true
  only at the default.
- **It is redundant.** Every consumer that wants a scaled domain already has the
  knob for it. An oscillator's `freq` multiplies the phase advance, which is the
  same arithmetic one socket further along; anything else takes a Multiply, which
  is how every other signal in this instrument is scaled.

## Decision

**Time has no inputs. It emits `t`.** Scaling is a `math.mul`, exactly as it is
for every other signal — and into an oscillator's `in` it needs no scaling at
all, because `freq` is that multiply already.

**The presets say what they mean.** Where a preset ran a clock slowly it now
carries a Multiply with the same constant, and the ones that ran at 1 lost a
knob that did nothing. Three of them had two or three Time modules at different
rates; those are now one Time fanned out to two or three Multiplies, which is
what they always were — the compiler shared the single `LoadT` between them
regardless.

**Nothing is migrated, because nothing needs to be.** A saved patch keeps its
`rate` value in `InputValues`; the compiler reads knobs with a bounds check and
falls back to the port's default, so an extra value is ignored rather than
mismatched. This is the same tolerance `AudioScan.For` already documents for a
definition that has lost a socket.

## Consequences

**Old patches that set a rate run faster.** The saved value is ignored, so a
patch that scaled time by 0.15 now runs at 1. It is silent as changes go — the
file loads, compiles and renders — and it is the one real cost here. The fix in
each case is a Multiply, which is what the patch would say now if it were built
today.

**A slow picture costs a node.** That is the trade taken: one more module in a
patch that wants slow motion, against a class of mistake that only an ear can
catch. The node is worth something on its own, though — where a patch runs
slowly is now visible in the patch rather than folded into a knob on a module
called Time.

**Every preset lowers to the same program.** Approved GLSL for all nine changed
only in register numbering, and the rate-1 presets lost a `× 1.0`. Nothing
renders differently, which is the check that this was arithmetic moving rather
than behaviour changing.

**A module's shape changed, and that is now known to be cheap.** Ports are data
([0008](0008-modules-as-data-in-one-catalogue.md)) read by index with a fallback,
so removing one costs a re-approval of the shader snapshots and nothing else. It
is worth recording that this was cheap, because the next question of this kind
will be whether some other knob is a second control in disguise.
