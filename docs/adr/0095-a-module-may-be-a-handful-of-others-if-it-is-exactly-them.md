# ADR-0095: A module may be a handful of others, if it is exactly them

**Status:** Accepted · 2026-09-18 · *user-directed* · follows
[0008](0008-modules-as-data-in-one-catalogue.md) and
[0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md)

## Context

The catalogue has been a catalogue of primitives. [0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md)
refused a module wherever the maths already had the combinator, and that refusal
is right for a vocabulary. It is paid for in the patches. The eight showcase
presets are 1,469 modules, and 357 of them are Multiply, 135 Add and 133 Remap:
a reader opening the Kick box finds a Fraction, a Subtract, two Powers, a Remap,
a Sine and a Drive, and has to work out that it is a kick.

Counting what is wired to what across those eight says the same few things are
being built over and over. An envelope read off the beat — Fraction, one minus,
Power — twenty-seven times in four tracks. A desk built twice, once for each
ear, with a trim and a Clamp on each: thirty-nine Mixers, in seven presets of
eight. The last frame through a Scale and a Rotate into a Gain and a Maximum, in
every one. White noise out of a Sine and a Fraction in six, because Random's
white comes with a pink nobody asked for. A Noise pinned by a Value and rescaled
by a Remap for every slow voltage. A Smoothstep into a Multiply for every part
an arrangement brings in. A swept Sine into a Drive for every kick.

`PresetBench` had already named most of them — `Stroke`, `Rises`, `Tone` — which
made the C# read well and left the canvas exactly as it was.

## Decision

**Seven modules that are each a handful of others: Stroke, Fade, Hiss, Wander
and Drum in Voice, Desk and Trails in the engine.** None has an opcode, a cell it
did not already need, or an idea of its own. Each is the arithmetic of the
modules it stands for, emitted in the order they emitted it.

**Exactly, which is the condition.** A preset moved onto one of these is heard
by ear and looked at by eye, and "the same" has to mean the same samples and the
same bytes. So every knob at rest is an identity the arithmetic keeps — times
one, plus nought, a turn of nothing, to the power of one — and every one of the
seven has a test that builds the long form beside it and compares with equality
rather than a tolerance. Where an identity would not hold the module does
something else instead: the Drum steps its Drive out at nought rather than
mixing it out, because a Mix at either end is its operands only nearly.

**A module earns this by being found, not by being imagined.** Each of the seven
was a pattern in at least three presets or a dozen instances. What was counted
and left out: an FM bell pair (nine of ten in one preset), an oscillator into a
Remap (an oscillator's `amp` and `bias` already do it), and a whole synth voice —
every voice orders its Filter, Drive and gate differently, so the wrapper would
fit none of them.

**Desk and Trails are the engine's because Mixer and Feedback are.** The engine's
own presets may not need a plugin, and Nebula and Whole band close with a tail
and a desk like everything else. The five in Voice sit beside the modules they
are cheaper or stateless versions of: Hiss by Random, Wander by Random's drift,
Stroke by Decay.

**Desks chain by a bus rather than growing.** Four stereo channels is fifteen
sockets, and eight would be a module taller than most patches. Instead a Desk
hands on its sum before its trim and its rails, and the next adds that in at
unity, so a chain of three is one desk of twelve in which only the last trims
and only the last can clip — which is also, to the bit, the three Mixers into a
fourth they replace, because a sum of two is the same either way round.

## Consequences

**The primitives stay, and stay the specification.** Nothing is removed, a patch
built the long way is not wrong, and what one of these modules means is still
"what those five would have done". The tests that say so are what stop the two
drifting apart.

**Hiss does not promise what Random promises.** Random reads the engine's hash,
identical on every backend. Hiss is a sine of a number in the millions with its
last digits kept, and a shader's float has fewer of them than a double. It is
the same hiss and not the same samples, which for a hi-hat is fine and is why
Random is still there.

**Stroke is not Decay.** It falls over a share of a beat, not over a time, and
cannot be struck off the grid. What it buys is that it has no memory, so the
ring a kick pushes and the kick are one number on both sinks
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)).

**A wrapped module hides its middle.** A patch that wanted the Stroke's phase
before the power, or the tail before the Maximum, gets those as second outputs;
one that wants something else in the middle builds the long form, which is
still there.
