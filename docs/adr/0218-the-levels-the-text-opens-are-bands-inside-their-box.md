# ADR-0218: The levels the text opens are bands inside their box

**Status:** Accepted · 2026-11-01 · *user-directed* · builds on
[0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md)

## Context

The text nests: a def inside a def, a loop or a group, and a group inside a group or a
loop. The canvas drew only the outermost box, so an inner group, def call or loop pass
was loose inside it with nothing saying which level a module belonged to. Arek chose a
design on a Claude Design canvas (banded levels with shelf chips).

## Decision

**The binder records each level it opens** (`Level`, `LevelTrail`): groups, def calls with
the arguments they were given, loops, and each loop pass with the item it took.
`flyback-cli levels` prints the tree, `--of` one module's path, and `--json` both.

**Inside an open box each level is a band**, laid left to right as its wires run, under a
shelf with a chip naming it: a group's name, a call as written (`bass = voice`), a pass as
`1/3 pitch = 110`. A level that placed nothing and holds one level shares its chip. A box
is laid out open when it holds a level, shut otherwise; there is one layout.

**A shut box lists the first levels inside it** along its foot, and the pass that made it,
the rest as `+N`. The foot rather than under the header, so its sockets keep the rows the
layout and every wire already use.

**Pressing a chip, or a level in the inspector's path, selects the level.** The inspector
then shows the level: what it was given, what is inside it, the wires leaving it, the other
calls of its def, and a way to the line in the text. A box keeps its own panel, with the
path above it.

**Zoomed far out only each box's outermost chips show**, at a size that reads.

**Levels exist only while the text owns the patch.** The canvas is locked then, so nothing
is ever dragged out of a band; when the canvas takes the patch back the chips go and the
modules stay where they were.

## Consequences

- Live patching draws no chips: it shows only the view that owns the patch, and a patch
  with levels is owned by its text.
- A box inside a box, a faint outline per call, opening a box into a view of its own and
  highlighting on hover were ruled out: wires crossing several edges, clutter, a drastic
  change of view, and nothing to hover on a touch screen.
