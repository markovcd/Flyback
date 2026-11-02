# ADR-0219: The editor looks like an instrument on the bench

**Status:** Accepted · 2026-11-02 · *user-directed* · supersedes the wash and the mark of
[0116](0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md) and the mark
[0122](0122-the-panel-wears-the-block-it-is-about.md) borrows from it

## Context

Arek said the editor looked designed by an LLM. It was stock Fluent with nothing
overridden (the system's blue accent, rounded corners, a row of identical grey toolbar
pills), and its modules wore the look every node editor shares: a rounded block with a
solid colored title bar, a gradient body and a large faint glyph behind the rows, the
same glyph again behind the inspector. None of it was chosen for a synthesizer named
after a CRT's return stroke.

## Decision

The editor is drawn as an instrument, with a scope on the bench as the reference
("Phosphor"). Arek picked the direction from three drawn on a design page.

- **The controls are Flyback's.** Fluent takes `Colors.Attention` as its accent and
  2 px corners; toolbar and panel glyph buttons have no fill until pointed at or
  switched on. Number boxes and the clock are set in the mono face.
- **A module is a panel, not a node.** Square corners (2 px), a flat body with a
  slight tint of its category, and a dark title band with a 3 px rule in the
  category's accent along its top. The rule is what tells the kinds apart.
- **No mark.** Neither the canvas nor the inspector draws a category's glyph behind the
  rows. The small glyph beside a module's name in the inspector stays.
- **The grid is a graticule.** A line every fifth division, and the divisions between
  as ticks across those lines.

A plugin's own background ([0118](0118-a-plugin-paints-its-own-module-background.md))
is unchanged.

## Consequences

- Zoomed far out, a module's kind is the rule and the tint, which is less than the old
  colored band gave; the large name of the overview zoom carries what it is.
- Wires glowing with the signal they carry was proposed and held back: the canvas would
  repaint every frame and the engine would have to report a level per output. It is a
  feature of its own if wanted.
