# ADR-0235: A list is read by position, and a block reads names

**Status:** Accepted · 2026-11-16 · *user-directed* · extends
[0208](0208-the-text-has-loops-that-unroll-as-it-is-built.md)

## Context

A loop could walk a list only by taking each item whole, so a table carried
through a loop had every column packed into one tuple, and nothing could count
it. A sequencer's block was read as written, so a loop or a def could vary every
knob of a pattern but not its notes: `notes() [ root ]` was refused as "not a note".

## Decision

**`xs[i]` reads an item, counted from one, and `len(xs)` counts them**, both as
the text is built. Counting from one matches the ranges a loop counts over
(`1..len(xs)`) and the passes groups are numbered by. The bracket must touch what
it reads, so `name [ ... ]` after a space stays a module's block. A position is a
whole number the text writes down; a signal, a fraction or one past either end
is refused. `len` is a word only where nothing else is called it, as `sum` is.

**`$name` in a block writes in what the name stands for** before the block is
read: a number or a note as itself, a list as its items one after another. The
notation after it applies to what it wrote last. A signal, a text or a duration
is refused, since a block holds steps the text knows.

## Consequences

- A block that reads a name is not rewritten when its steps are edited on the
  canvas: writing the steps back would replace the name with what it stood for.
  Turning a knob on the same module still writes back.
- A number read by position counts as read through a name, so one read twice is
  not a knob's to turn.
- A printing writes the steps out and the items in place, as it unrolls a loop
  (0065).
- A complaint about the notation after a `$name` may point a few columns off,
  since the block is read with the name written out.
