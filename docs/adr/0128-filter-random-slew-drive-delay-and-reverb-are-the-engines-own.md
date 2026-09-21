# ADR-0128: Filter, Random, Slew, Drive, Delay and Reverb are the engine's own

**Status:** Accepted · 2026-09-21 · *user-directed* · amends
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) and
[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md)

## Context

[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) put the filter in a
plugin because a plugin could hold state at all: two cells from
`Emitter.AllocateUnitSlot`, nothing more. Slew and Drive followed the same
reasoning into Voice, and Delay and Reverb — a buffer rather than a cell, but
still no new opcode — into Effects. All five, and the stateless Random beside
them, were built to be exactly the kind of module [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)
says a plugin can hold.

[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md) put
Desk and Trails in the engine instead of Voice, on one sentence: "the engine's
own presets may not need a plugin." `Presets.WholeBand.cs` is the case that
sentence was written for and did not yet fix. It hand-builds a one-pole lowpass
(`Smoothed`), a highpass (`Thinned`) and white noise from `fract(sin(clock))`
— the filter and the noise a plugin already has, reimplemented by hand because
an engine preset cannot reach into `Flyback.Plugins.Voice`. The Delay and
Allpass opcodes it uses directly already live in `Flyback.Core`; only the
module wrapping them was a plugin's.

Nothing about any of the six is particular to a voice or an effect. A filter
takes harmonics away from anything, not only a Voice oscillator; a delay
repeats anything, not only what Effects built. They are primitives the way
Duck ([0125](0125-a-duck-is-a-sidechain-with-the-depth-on-a-knob.md)) is a
primitive, and Duck is already in the engine.

## Decision

**Filter, Random, Slew, Drive, Delay and Reverb move into `NodeCatalog`,** one
partial file each (`NodeCatalog.Filter.cs`, `.Random.cs`, `.Slew.cs`,
`.Drive.cs`, `.Delay.cs`, `.Reverb.cs`), registered in the static constructor
beside Duck. Behavior is unchanged to the bit: the same ports, the same
defaults, the same ops in the same order, the same extras. Only the id, the
provider and the file moved.

**New ids in the engine's own naming: `audio.filter`, `audio.random`,
`audio.slew`, `audio.drive`, `audio.delay`, `audio.reverb`.** `audio.*` is
already how the engine names a signal-processing primitive that is not tied to
one category of sound — `audio.duck`, `audio.tune`, `audio.frequency` — and
each keeps the category it had (Filter and Drive under Shaping, Random under
Oscillators, Slew under Timing, Delay and Reverb under Time effects).

**An old id still opens.** `NodeCatalog.LegacyTypeIds` is a six-entry map from
the old provider-prefixed id to the new one. `ModuleCatalog.Get` — and so
`Require` — falls back to it wherever the exact id is not found, which is what
lets the compiler, the text language and the assistant's tool calls resolve an
old id without knowing it moved. `PatchIO.Read` goes further and rewrites a
loaded node's `TypeId` outright, and recomputes `Requires` when it does, so a
file that named `flyback.voice` or `flyback.effects` only for one of these six
is not refused where that plugin is absent, and saving writes the new id.
Clipboard and bundle round-trips go through `PatchIO.Read`/`ToJson` already, so
both take the alias for free.

**What a plugin built on one of these now calls the engine's own copy.**
`FilterModule.Responses`, `RandomModule.Noise` and `DelayModule.Echoed` — the
"one module's worth of ops" helpers a wrapping module reused — are now
`NodeCatalog.FilterResponses`, `NodeCatalog.RandomNoise` and
`NodeCatalog.DelayEchoed`, public on the contract. Voice's Hiss calls the first
two; Effects' Echo calls the third. Neither plugin still defines Filter,
Random, Slew, Drive, Delay or Reverb, and `VoicePlugin`/`EffectsPlugin`'s
`Info` say so.

**Whole band and the engine's other presets are not ported.** Their sound has
to stay byte-identical, which is a separate, measured job; this ADR only makes
the modules available to port onto later. They still build and their tests
still pass.

## Consequences

- A patch can filter, randomize, glide, drive, delay or add a reverb with no
  plugin installed at all — the six primitives most voices and effects are
  built from are the engine's now.
- Voice keeps the seven-oscillator supersaw, the FM voice, the drum, the bell,
  the fold, Hiss, and the timing and rhythm modules; Effects keeps the tempo
  echo, chorus, flanger and phaser — everything built *from* the six rather
  than being one of them.
- `Flyback.Core`'s public surface grew by six constants and three helper
  methods, recorded in `PublicAPI.Unshipped.txt` — the first addition since the
  1.0.0 release, so `PluginContractVersion` moves to 1.1.0
  ([0102](0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)).
- A saved patch, a pasted fragment, a bundle or a line of text naming one of
  the six by its old id still means what it said; the file it is next saved as
  says so under the new one.
