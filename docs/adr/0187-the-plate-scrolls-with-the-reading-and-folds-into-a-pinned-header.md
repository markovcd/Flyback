# ADR-0187: The plate scrolls with the reading and folds into a pinned header

**Status:** Accepted · 2026-10-08 · *user-directed* · supersedes
[0122](0122-the-panel-wears-the-block-it-is-about.md)'s pinned plate

## Context

[0122](0122-the-panel-wears-the-block-it-is-about.md) pinned the plate above the scrolling
reading so the name and the buttons were there at every scroll position. On a phone held
sideways the inspector is about 120 dp tall and the plate about 135, so a selected module
showed its name and its buttons and nothing else: no knob, no scrollbar. The redesign came from
a design pass handed the panel's sizes at each device.

## Decision

**The plate is the first thing in the scroller**, so the reading can always be reached by
scrolling it away.

**A 48 dp header stands in for it once it has gone**: the block's mark, its name and what kind of
thing it is, then on/off (open or close, for a box), delete and ⋯, each 44 across. It pins in once
the name band has scrolled past, and from the start on an inspector under 200 dp tall, where the
plate is not shown at all and the description moves into the menu. `InspectorFold` decides.

**The ⋯ menu holds every action as a glyph with a word under it**, in Module and Selection
groups, with Rename among them, since a finger cannot find a double-click. Tapping the name opens
it too.

**The header and the menu mirror the plate's own buttons** by name (`PlateActions`) rather
than build actions of their own: a tile presses the button it stands for, so what is offered and
what pressing it does are decided once, where
[0111](0111-the-panels-actions-are-a-row-of-glyphs.md) put them.

**The wash still paints the band**: as deep as the plate's name band less what has scrolled, or as
deep as the header while it shows, so the header has no paint of its own.

## Consequences

- A tall panel looks as it did until it is scrolled; the desktop's shots are unchanged.
- The menu's tiles and the header's buttons are found by `menu-` and `header-` and the plate
  button's name.
