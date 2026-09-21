# ADR-0116: A module is drawn as its category, and a standout as itself

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

Every module on the canvas was the same rectangle: `Colors.Node` grey, an
outline, and a 26-pixel header in its category's accent. The accent was the
whole of what told an Oscillator from a Geometry module, and it was a band
across the top ten percent of a block that is usually a hundred pixels tall.

Two things follow from that. Reading a patch means reading titles, because the
body carries no information at all — twenty grey rectangles with colored
caps. And zoomed out far enough to see a whole patch, which is where the big
presets live, the titles are gone and the headers are a row of tick marks:
Nebula's site screenshot is fourteen modules of which not one is identifiable.

A group had the same problem from the other side. A shut box was a module with
a grey header, and an open one was a dashed ring with its name floating over
the canvas above it, joined to the region by nothing.

## Decision

**A module's body is washed in its category's accent**, mixed into the node
grey — 24% under the header falling to 6% at the floor, through
`Colors.Blend`. Slight on purpose: at full strength the patch is fourteen
colored rectangles with the labels lost in them, and what is wanted is a
difference legible at a glance rather than a poster.

**A mark is set in the body**, large, in the accent at 17%, behind the socket
labels and under the header. Drawn on a twenty-four unit box in `ModuleGlyphs`
and stroked, so one scale takes a path to whatever size it is wanted at; sized
to the body so a one-socket module gets a small whole mark rather than the
bottom third of a large one, and capped at 52 units so a tall module's mark
does not become the module.

**A module earns a mark of its own by being the only one of itself.** The four
fixed waveforms, the pulse and the plucked string, the clock and the plane
everything is normalled to, the sink, the turn a Feedback reads and the loop a
Scan reads, the two ends of a bus, the braces of an Expression, the four
sources that are not a computed signal, the tempo and the sample-and-hold, the
desk that sums four of anything, the noise field, the sidechain duck, the
scale quantiser, the random, the filter and the drive, the delay and the
reverb, and Measurement's five ways of looking at a signal — two charts, a
spectrum, a level and a loop. A shipped plugin's standouts earn theirs the same
way, as a `Palette` skin in their category's own accent
([0118](0118-a-plugin-paints-its-own-module-background.md)). **Everything else is drawn as its
category**, which is fourteen more paths — a wave in a ring for the
Oscillators, an envelope for Timing, a transfer curve for Shaping, decaying
repeats for the time effects.

**A category nothing here knows draws nothing.** A plugin may name its own
category, and it already falls back to grey in `Colors.Accent`; inventing a
shape for it would be worse than leaving the body bare. A plugin that wants
better than grey says so itself, by painting its module's background —
[0118](0118-a-plugin-paints-its-own-module-background.md).

**Both bands get a face**: a white hairline along the top of a header, held
off the rounded corners, and a dark seam where it meets the body.

**A group is drawn as a region rather than as a line.** The open one's wash is
a gradient brightest where it meets the strip above it, and its name sits on a
tab only as wide as the name — a header is what a group wears when it is shut,
and a bare name over the canvas belongs to nothing. The shut box keeps the one
color on the canvas that belongs to no category, now as a gradient, and takes
the toolbar's own `Group` glyph as its mark.

**The brushes are cached per category in `NodeSkin`.** Painting asks for them
per module per frame and a gradient brush is not free to make; the canvas is
one control on one thread (ADR-0017), so the caches need no guard.

## Consequences

**A patch is readable zoomed out.** The tint and the mark are what survive at
a zoom where no label does, which is the case this was decided for — a
showcase preset seen whole.

**The marks are artwork, and artwork fails quietly.** Avalonia draws nothing
for a path it could not read and says nothing about it, and an arc whose
sweep resolves the other way lands outside its box. `ModuleGlyphsTests` renders
every mark and checks it is a shape of some size inside its own twenty-four
units, that every category has one, that no two modules share one, and that a
mark keyed to a type id names a module that exists — which is how a renamed
module loses its drawing.

**`Colors` gained three mixing functions.** `Blend`, `Shade` and `Faded` are
still colors, not brushes, which is what the file is for: what they make are
gradient stops, and a stop has one color.

**A label is read over a mark.** 17% is where a mark is plain at a glance and
a normalled module's name — "Coordinates y", the longest thing drawn in that
column — is still read without looking past it. Raising it makes the marks
better and the busiest modules worse.

**The site screenshots showing a patch were retaken.** `site.css` draws no
module of its own —
[0119](0119-the-website-shows-a-module-by-photographing-one.md) replaced the
drawn node cards with photographs of the real canvas — so the wash and the
mark reach the site the same way any other change to a module's appearance
does, the next time its shots are retaken. One was left: `settings.webp`,
whose canvas is under a dialog's scrim.
