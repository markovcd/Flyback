# ADR-0152: Easy Synth is a whole synth that cannot be set wrong

**Status:** Accepted · 2026-09-28 · *user-directed* · follows
[0146](0146-fractals-is-three-modules-about-one-point-c.md) and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)
for how a plugin reaches anybody

## Context

The most idiot-proof oscillator was asked for: a wave picked from a list, a
built-in envelope, LFOs and a filter, and whatever else helps, as a plugin of
its own that only the preset site publishes. A drum is to come later, as a
module of its own. A synth built from the engine's modules is six or seven
of them and a dozen wires, and most of the ways to wire it are silent or
deafening.

## Decision

**One module, Easy Synth, in a plugin called Easy**, where the drum will join
it. It is an oscillator, a sub and noise, an ADSR, a resonant filter, two
LFOs, glide, drive and pan, with 'left', 'right', 'env' and 'lfo' out.

**It sounds good with nothing wired.** The gate is up by default, so it drones
an A3 saw through a plucked low pass. The pitch is a note number, so a MIDI
In's or a Note Sequencer's note wires straight in, and 'velocity' is named for
the socket that feeds it.

**No knob, wire or setting takes it past full scale.** Every input is clamped
before it is used; the note is held on the keyboard after the octave and the
vibrato; the sub and the noise are shared out with the wave rather than added;
the filter is turned down as the resonance rises, and the band is scaled to
peak at full; drive is faded in and divided back out; and the last thing done
to each side is a clamp to -1..1.

**'bright' means how much gets through, whichever filter is kept.** The corner
follows the note, so every key is as bright: a low pass opens upward from half
an octave over the note, a high pass opens downward onto it, and a band climbs
from it. The envelope's 'sweep' and an LFO sent to the filter open it further
the same way.

**The wave, the filter and where each LFO goes are settings on the node**, as
lists, because they decide what is emitted (ADR-0097). An LFO goes to the
pitch, the filter, the volume or the pan. On the pitch its depth is squared, so
the bottom of the knob is a vibrato and the top a siren; on the volume it only
ever dips from full.

**The envelope, the glide and the filter keep cells**, so the screen gets the
gate, the note and the unfiltered wave, and everything else is the same
arithmetic on both sinks.

**It wears an artwork of what it is**: a filter's response in the gap beside
the outputs, and a filtered saw running down the block, shaped by its
envelope. SVGs, drawn a shade under the node gray so white labels read over
them.

**The preset site starts with it, like Figures and Fractals.** No release
carries it.

## Consequences

- About 290 ops on the speakers for the preset, one module; a Supersaw doubles
  the filter to keep both ears apart.
- The envelope's stages reach thirty seconds, as the ADSR's do, so an attack
  turned all the way up is a long silence first.
