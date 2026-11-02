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

- **The accent is P31 green, #77EA9A.** It is the Beam module's own trace at its default
  hue, taken where the trace begins to whiten, so it comes from Flyback rather than from
  taste. Amber was tried first as the default and dropped: Arek's other app, also built
  with Claude, had landed on near-black and an amber a few units away, which is the
  generated default this decision exists to avoid. Oscillators moved to a darker sea
  green so a selected one still shows its ring.
- **Three more color schemes stand beside it**, in Settings, Canvas, Colors. Arek picked
  them from palettes a generator gave with nothing locked: Amber (teal-black grounds,
  cream text, #D1AC00), Graphite (charcoal, sage text, #FF7F11) and Midnight
  (violet-black, warm grays, #F46036). Phosphor stays the default. A scheme changes
  the grounds, the text and the accent, never a module's category color, and takes
  effect at the next start: brushes made from the colors are cached for the run, and
  repainting every one of them live is not worth what it would cost.
  `flyback-cli shot --colors` draws the window in any of them.
- **A scheme is data.** Each is a JSON file of seventeen `#RRGGBB` colors by role under
  a `format` and a `name`; the four are shipped inside Flyback as such files, and any in
  the schemes folder beside the settings are offered after them, a file winning over a
  shipped scheme of its name, as an instrument profile does. `format` rises only when an
  older reader would get a file wrong; a file from a newer Flyback is skipped whole, and
  a color a file lacks or gets wrong is Phosphor's, with the report bar saying which.
  `text.bright`, for a value being turned or typed, a heading and a selection's outline,
  may be left out and is then the label taken most of the way to white, or to black in a
  scheme whose file sets `light` true. That flag puts Fluent's controls in their light
  variant and turns the editor's relief around: `Colors.Lift` (a hover wash, a glint, an
  edge) is white on a dark scheme and black on a light one, and `Colors.Press` (a
  shadow, a seam, the scrim) keeps black but at half strength on a light one.
- **The interface is drawn only in the scheme.** A module family's color stands for a
  module and nothing else: the assistant, links and a finished tutorial step take the
  accent. A fault is one fixed red, `Colors.Fault`, kept apart from the Output family's
  so it never reads as a module. `SchemeColorTests` fails on a color written out in the
  editor's code or a family color used outside the canvas; shadows, glints and the logo
  are exempt.
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
- **Toolbar symbols are panel marks.** Drawn on a 16-unit box at weight 1.6 with square
  caps and miter joins, a solid fill where a hardware legend would have one, and the
  name in small bold capitals under each. The names are a setting, on by default; a
  window too narrow for them drops the names before folding any button.
- **The inspector opens on the values.** The name and the family share one line, the
  buttons stand in their own row under it on the band, and the description follows
  the outputs, since it is read once and the values every time.

A plugin's own background ([0118](0118-a-plugin-paints-its-own-module-background.md))
is unchanged.

## Consequences

- Zoomed far out, a module's kind is the rule and the tint, which is less than the old
  colored band gave; the large name of the overview zoom carries what it is.
- Wires glowing with the signal they carry was proposed and held back: the canvas would
  repaint every frame and the engine would have to report a level per output. It is a
  feature of its own if wanted.
