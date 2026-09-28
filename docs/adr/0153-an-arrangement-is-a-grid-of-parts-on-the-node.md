# ADR-0153: An Arrangement is a grid of parts on the node

**Status:** Accepted · 2026-09-28 · *user-directed* · a fourth engine kind of
what a module carries, beside [0038](0038-a-sequencers-notes-are-a-list-on-the-node.md)'s
notes and [0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)'s scale, under
[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md)

## Context

A piece that changes over minutes needs each of its parts brought in and out by
section. Every showcase did it the same way: one Sequencer stepping once every
eight bars, its value a made-up intensity, and each part decoding that number
back into "am I playing?" with thresholds. Warehouse had about eighteen formulas
like `step(0.45, a) * (1 - step(0.6, a))` for "this is a build", and four more
one-step-a-phrase Sequencers for things that were rows of the same table; the
Effects showcases decode theirs through `PresetBench.Enters` 38 times.
[0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md) looked at the
arrangement's Sequencer and left it out, because a wrapper could not hide its
steps.

The information was always a table: parts down the side, sections across.
Squeezing it into one number and unsqueezing it per part is the workaround for a
module nobody had.

## Decision

**An Arrangement module in the engine, Timing, `seq.arrangement`.** It carries up
to 8 parts of up to 32 sections, each cell a `PartLevel(Value, Glides)`, and
steps on `in` at `rate` sections a unit like a Sequencer: Time without a wire, or
a Tempo's beats. It has an output for each part, `progress` through the section
and `section`, counting from 1.

- **Stateless**, as [0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md) set
  out, so it runs on the picture too. Each part is where it is and where it came
  from, each a chain of Mixes on section edges every part shares, blended by how
  far the change has got: an op for each change of level, nothing for a section
  that holds, and one constant for a part that never changes. A sum of windows
  like a sequence's would cost two ops a section whatever the part does, and
  Warehouse on it came out 8% heavier on the sound and 39% on the picture than on
  the Sequencers it replaced; on chains it is 5% lighter on the picture and
  within 1% on the sound.
- **A level that differs from the section before's is reached over `fade`**, a
  fraction of the section held above a thousandth so that no change clicks, or
  **across the whole section where it glides**, which is a build. Section one's
  "before" is the last section, which is where a looping arrangement comes from.
- **Tidied on the way to the emit**: parts shorter than the longest hold at nought
  to the end, and anything past 8 by 32 is dropped.
- **Its own block in the text**: a row of levels a part, `|` between parts, and
  `>` before a level that glides. A level is only ever a number: `~` is a rest in a
  sequencer's block, and nought here would be a different thing by the same name. Only `|` ends a part, so a long
  one runs over lines, and a printing too long for one line breaks at each `|`.
- **Its own control in the inspector**: a map of every level shaded in the
  module's accent, and a row of the text's levels to type into for each part.
  A grid of up to 256 knobs would be read as nothing at all.
- **Its own tool for the assistant**, `set_arrangement`, taking each part as one
  string in the text's notation, which every provider's schema can say.

A plugin's generic fields ([0055](0055-a-plugins-extra-declares-its-editor.md)) were no fit: they are a number, a choice, a
switch or a line of text, and none holds a list, let alone a grid.

## Consequences

A piece's shape is written once, readably, and each part reads its own row
instead of a threshold scheme only its author follows. The engine now has five
kinds of carried state with their own code in the binder, the printer, the
inspector, the text write-back and the assistant; the next one pays the same.
The outputs are numbered rather than named, since a port's name is the
definition's and a part's name would be the patch's.
