# ADR-0122: The panel wears the block it is about

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

ADR-0116 gave a module a face: a category wash down its body, a band across its
header, and a mark set large and faint behind the labels. ADR-0118 let a plugin
paint that face itself. Both of those stopped at the canvas.

The inspector is where a module is actually worked on, and it showed none of it.
A name at 17 points, the category as one small line of accent text, a row of
glyph buttons and then the knobs — all of it on `Colors.Panel`, the same flat
grey the assistant and the status bar are drawn on. Selecting a module meant
looking away from a block that says what it is at a glance to a panel that says
it in words, and the two did not look like the same thing. On a patch of
fourteen modules the panel gave no help at all in answering which one is open.

## Decision

**The top of the panel is the block's own face.** `ModuleWash` paints all of it
— the band, the light along its top edge and the seam under it, the background
below and the mark — every one of them out of `NodeSkin`, so there is no second
set of colors to drift. The band is square and runs edge to edge: a block on a
canvas is a rounded thing among others, and this is the top of the panel itself.
`ModulePlate` holds only what stands on that face: the name, what kind of thing
it is, and the row of buttons.

**The plate is pinned and the reading scrolls under it.** It is docked above the
scroller rather than being the first thing in it, because what a block is and the
buttons that act on it are wanted at every scroll position — and because a face
that scrolled away would leave the panel saying nothing about what is being
edited.

**The band carries the name and what kind of thing it is.** Both lines stand on
it, the second quieter, because the band is the container that says which block
this is. The body under it holds the row of action glyphs and the mark, and
everything below the plate — the description, the sliders, the readings, the
keyboard — stays on the panel grey. A column of numbers is read rather than
glanced at, and a wash behind every row would be a decoration in the way of the
thing it decorates.

**Nothing is drawn round the plate, and the background is the whole panel's.**
A block on the canvas is ringed because it stands on a canvas with other blocks;
the plate is the head of a panel, and what ends it is the reading running on
underneath. So the wash is not the plate's at all: `ModuleWash` sits behind
everything, fixed while the reading scrolls over it, and fades from full strength
in the top right corner to nothing by about the middle — under one opacity mask,
so a wash, a grain and a plugin's picture all give way the same way. A fill that
stopped where the plate does would end on the line the plate does not have.

**The fade's angle is in pixels, not in fractions of the panel.** A gradient given
relative ends is skewed by the shape of what it fills, so the angle would tilt
every time the splitter moved. The angle is fixed and steeper than the diagonal;
only how far it runs follows the panel.

**The mark belongs to the wash, four times the size the canvas draws one.** There
is one block on the panel where a patch has forty, so it is drawn large, under the
plate, against the right-hand edge — and outside the mask, since it is the one
thing here that has to be there whatever the fill has already given up. Not over a
picture, for the reason the canvas does not draw one there: the background is the
author's, and a category's mark laid over it is the shell drawing on somebody
else's artwork.

**The name and what kind of thing it is are read down the right edge**, where the
fill is at full strength and where the eye is already going for the mark.

**A name that can be renamed says so, and renaming does not move it.** The
pointer turns to a hand over it, and the box a double-click puts there is dressed
as the name it replaces: same size, same weight, same ink, no fill, no border and
no focus ring. The theme paints those on a part inside the box's own template, so
undoing them takes a style (`ModulePlate.Naming`) rather than properties on the
box.

**The panel's watermark is gone.** The `LogoMark` behind the inspector was there
because the panel was empty grey; the block's own face is what fills it now, and
two faint drawings behind one column of numbers is one too many. The mark itself
still opens the About window.

**The plate is drawn unselected.** Everything the panel ever shows is selected,
so the selected ground and the attention ring would be a state that is never
off. What the plate is for is saying which block this is.

**A box gets the same plate in the canvas's greys.** A group belongs to no
category (ADR-0116), so there is no accent to carry over: it takes
`NodeSkin.Box`, `BoxHeader` and `BoxMark`, which is exactly what the canvas
draws a shut box with.

**The band is as tall as whatever stands in it**, its own padding included. The
panel sets the name at `Text.Title` and the plate follows, rather than a second
number here having to agree with that one. The body is held to a floor of 44 so
the mark is there on a block with almost nothing to set — the Output has a
category and no more, and a face with no mark is the one block that cannot be
told at a glance.

## Consequences

The name and the row of buttons are no longer children of the inspector panel,
so the two places that swapped a control in where it stood — renaming, and the
question that replaces a kept group — take the panel their control is actually
in rather than the inspector.

`NodeSkin` gained what the canvas and the plate share: `Edge`, `Box`,
`BoxHeader`, `BoxMark` and `Relief`. `NodeEditor` had private copies of all five
and now has none. `Fade` is the panel's alone — a background that ends in
nothing is no use to a block that has to be picked out of a patch.

The screenshots on the website that show the panel — `plasma-inspector`,
`plasma-code`, `tutorial-canvas` and `tutorial-text` — are stale until they are
retaken from the real app.
