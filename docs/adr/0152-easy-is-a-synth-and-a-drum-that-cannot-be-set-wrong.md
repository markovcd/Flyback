# ADR-0152: Easy is a synth and a drum that cannot be set wrong

**Status:** Accepted · 2026-09-28 · *user-directed* · follows
[0146](0146-fractals-is-three-modules-about-one-point-c.md) and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)
for how a plugin reaches anybody

## Context

The most idiot-proof oscillator was asked for: a wave picked from a list, a
built-in envelope, LFOs and a filter, and whatever else helps, as a plugin of
its own that only the preset site publishes; then an idiot-proof drum in the
same plugin. A synth built from the engine's modules is six or seven of them
and a dozen wires, a drum part is a Tempo, a rhythm, an envelope and a voice,
and most of the ways to wire either are silent or deafening.

## Decision

**Two modules in a plugin called Easy.** Easy Synth is an oscillator, a sub
and noise, an ADSR, a resonant filter, two LFOs, glide, drive and pan, with
'left', 'right', 'env' and 'lfo' out. Easy Drum is one of eight drum sounds
playing a rhythm, with 'left', 'right' and 'env' out.

**It sounds good with nothing wired.** The gate is up by default, so it drones
an A3 saw through a plucked low pass. The pitch is a note number, so a MIDI
In's or a Note Sequencer's note wires straight in, and 'velocity' is named for
the socket that feeds it.

**Easy Drum plays in time with nothing wired.** Its rhythm defaults to Auto,
the one its sound is usually heard in: a kick on every beat, a snare and a
clap on two and four, closed hats on the eighths, open hats on the offbeats.
Nine rhythms can be picked instead, or Trigger, which plays on each rise of
'trigger' from a MIDI In or a sequencer.

**A rhythm is read off the clock, not kept.** The bar is sixteen sixteenths
at 'bpm'; for each step the steps since the last hit are known when the node
is compiled, so the time since the hit is arithmetic on the domain, and so is
each sound. Every Easy Drum at one tempo is on one grid, none can drift, and
the screen flashes with the hits the speakers play. 'swing' warps the grid so
every second sixteenth lands late. Only Trigger keeps the time since the rise
in a cell, so on Trigger the screen gets the trigger.

**Each sound is a function of the time since its hit.** The kick and the tom
are a sine falling in pitch, its phase integrated in closed form so every hit
starts at nought; the snare is two sines and high-passed noise, the clap three
bursts and a tail of band-passed noise, the hats and the cowbell squares at a
drum machine's inharmonic pitches through a filter, and the rim a short sine
and a crack. 'tune', 'decay' and 'tone' move every sound the same way, and the
eight are leveled to within two decibels of one another at their peaks.

**No knob, wire or setting takes either past full scale.** Every input is clamped
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

**Each wears an artwork of what it is**: the synth a filter's response in the
gap beside the outputs and a filtered saw running down the block, shaped by
its envelope; the drum a lit sixteen-step bar there and a kick's falling wave
across the block. SVGs, drawn a shade under the node gray so white labels
read over them.

**The preset site starts with it, like Figures and Fractals.** No release
carries it.

## Consequences

- About 290 ops on the speakers for First notes, one synth; a Supersaw doubles
  the filter to keep both ears apart. First beat, four drums into a Desk, is
  about 750.
- The drum's type id is `flyback.easy.drummer`, because the text language
  names a module by the end of its type id and `drum` is the Voice plugin's.
- A rhythm is one bar of sixteenths in four-four; anything else is Trigger.
- The envelope's stages reach thirty seconds, as the ADSR's do, so an attack
  turned all the way up is a long silence first.
