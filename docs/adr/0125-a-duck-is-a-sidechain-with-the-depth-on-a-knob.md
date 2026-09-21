# ADR-0125: A Duck is a sidechain with the depth on a knob

**Status:** Accepted · 2026-09-21 · *user-directed* · follows
[0100](0100-mastering-is-a-plugin-of-stateful-primitives.md); does not reopen
the sidechain [0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)
counted and left out

## Context

Six presets made room for a kick the same way: the kick's envelope into a
Remap, from one down to about a half, and that into a Multiply on each part. It
works only where there is an envelope to hand. A sampled kick, a bus or anything
else that is only a sound has none, and then the one way to duck was the
Mastering Compressor's `key`. That speaks in threshold, ratio and knee, and how
far it goes down depends on how loud the key is, not on anything the user set.

[0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)
counted the Remap and Multiply and declined to wrap them. That still holds for
the wiring. The missing piece was never a wrapper for it; it was a way to duck
from a sound.

## Decision

**Duck is a stateful engine module, under Shaping.** `left`, `right` and `key`
go in; `left`, `right` and `gain` come out.
- The key's level, `abs(key) / full` clamped to one, is followed by a one-pole
  lag with its own attack and release.
- The gain is `1 - depth × followed`. So `depth` is exactly how far a key at
  `full` takes the level down, whatever the key is.

**In the engine, not in Mastering.** Mastering is the end of a chain, and a
sidechain sits in the middle of the mix, beside the Desk. The engine's own
presets duck too, and an engine preset can use only engine modules.

**An envelope is a key like any other.** An envelope that runs from nought to
one, followed at the quickest attack and release, is itself. So Duck serves the
presets' idiom and a sampled kick alike, and the user has one thing to learn.

**The cell holds the followed level, not the gain.** A cell starts at zero,
which is a Duck at unity. Unkeyed, it is a wire from the first sample. On the
picture, which has no memory, it is a wire.

**The presets that ducked now duck with it.**
- The envelope ducks (Whole band, Dub, Acid, Outrun, Mycelium, Fracture) key a
  Duck with the envelope, at depth one less the Remap's floor and both times at
  their quickest. Its `gain` stands where the Remap stood, which keeps them
  within a hair of how they were tuned.
- Overworld's kick-keyed Compressor is a Duck keyed by the kick's sound, with the
  Compressor's attack and release. The master channel carries the two decibels
  of makeup.
- Slow weather's two followers stay built by hand: they are two of the five
  loops that preset exists to show.

## Consequences

- A sidechain is one module and three knobs a user already has words for.
- `gain` is an ordinary output, so a filter, a send level or anything else can
  be ducked by the same key.
- Duck is a sound-only module. A picture that pumps with the kick still reads a
  Meter ([0058](0058-the-picture-is-told-how-loud-the-sound-is.md)), at frame
  rate, which is the picture's rate.
