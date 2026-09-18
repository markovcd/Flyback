# ADR-0100: Mastering is a plugin of stateful primitives

**Status:** Accepted · 2026-09-18 · *user-directed* · builds on
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) and
[0027](0027-delay-lines-give-the-audio-path-a-memory.md)

## Context

Every patch ended the same way: a chain of Desks, then the Output. The only thing
between a mix and the speakers was the Desk's clamp to full scale and a second
clamp in the renderer, so a patch's level was set by ear with a trim and a
volume. Several presets said "limiter" when they meant a Drive in front of those
clamps. Nothing could turn a mix down by how loud it was, shape its tone as a
whole, or say how loud it came out. The site's tracks are aimed at -16 LUFS with
tools outside the repository.

## Decision

**A Mastering plugin with six modules**, for the stretch between the last Desk
and the Output: EQ, Width, Crossover, Compressor, Limiter and Loudness. They are
new stateful primitives rather than wrappers, so
[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md)'s
exactness and counting rules do not apply. Each keeps its state in unit cells
and delay lines, as [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)
has it, and none needed an opcode.

**Stereo is a left and a right, as on the Desk.** A cable is mono, so each
module that works on a mix takes both, with the right normalled to the left.
The Compressor and Limiter turn both sides down by one gain, worked out from
the louder side, so the stereo image does not lean. The Compressor's `key`
socket is normalled to its left. The emitter is handed the very slot 'left' is
when nothing is patched there, and that is how the module tells listening to
itself, which means both sides, from listening to a key.

**The Limiter is exact.** It delays the sound by its lookahead and holds the
lowest gain any sample still in the delay needs. The gain falls in a straight
line fast enough to get from unity to that aim within one lookahead, so no
sample is heard louder than the ceiling. The clamp after it exists for a
lookahead swept while it runs. The audio path is oversampled
([0023](0023-oversample-the-audio-path.md)), so its ceiling is close to a
true-peak ceiling.

**Every filter is one state-variable filter, mixed.** The EQ's shelves and bell,
the Crossover's Linkwitz-Riley sections and the Loudness module's K-weighting
are all the TPT topology the Filter already uses, with Andrew Simper's output
mixes. They are the same responses as textbook biquads, and they stay
well conditioned at a corner a thousandth of the oversampled rate, where a
biquad's coefficients run out of digits. At their defaults the EQ and the
Compressor are exactly wires.

**Loudness is measured twice, for two questions.** The Loudness module gives
BS.1770's momentary and short-term readings as signals, from running sums over
delay lines, for a patch to show or react to. Integrated loudness needs the
whole program before its relative gate is known, so it belongs to a render:
`flyback-cli render --loudness` measures what it writes and prints integrated
loudness and true peak. Both use libebur128's K-weighting, which is the
standard's table at 48 kHz and what ffmpeg's `ebur128` uses too.

**Not built:**

- Dither. It would sit before the renderer's decimating filter, which would
  take it back out, so it belongs to the float-to-16-bit step if anywhere.
- A multiband compressor as one module. Two Crossovers, three Compressors and a
  Desk are one.
- A clipper. Drive is a soft one, and the Limiter holds a ceiling.
- A mastering chain in one box. That is a wrapper, and nothing counts one yet.

No preset changes. A Limiter on a shipped track changes how it sounds, and that
is a decision for each track.

## Consequences

A patch can be mastered inside itself, and a render can say whether it hit its
loudness target without a second tool.

The assistant's briefing is at the four fifths of its budget that
[0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md) holds what
ships to. The six descriptions are kept short for that reason. The next module
to ship will have to raise the budget or cut something.
