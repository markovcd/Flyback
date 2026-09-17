# ADR-0092: A drawing too wide for the canvas shuts a box, and one that cannot fit moves nothing

**Status:** Accepted · 2026-09-17 · *user-directed* · finishes
[0044](0044-lay-patches-out-in-layers-not-with-springs.md) at the one edge it left
open, where the drawing is larger than the canvas it has to go on · bounded by
[0014](0014-coordinate-and-value-conventions.md)'s canvas

## Context

[0044](0044-lay-patches-out-in-layers-not-with-springs.md) says where every module
goes and never says what happens when the answer does not fit. `PatchLayout`
returned a bool for it and the button said a sentence, and that was the whole of
the treatment. Mycelium is the patch that shows it is not enough. Measured, with
the editor's own metrics:

| Mycelium — 295 modules, 23 boxes | Drawing | Canvas is 15000 × 10000 |
|---|---|---|
| As shipped, every box shut | 196 × 114 | fits |
| Every box taken off | 8708 × 9238 | fits, on 92% of the height |
| **Every box open** | **≈24600 × 4735** | **half again too wide** |

Only the third fails, and it fails across while using under half the room down.
The reason is that an open box is a ring drawn round a sub-drawing of its own, so
its width is its own column count — and a row of long ones multiplies.

What happened then was worse than the sentence suggested. `Arrange` wrote every
coordinate and *then* measured, and `NodeInstance.X` holds a coordinate inside the
canvas as it is set, so the four hundred-odd modules whose x had come out past
7500 were all set to exactly 7500: one vertical line of modules stacked on each
other against the right edge. Pressing the button made the patch worse than the
tangle it was pressed on, and then advised shutting a box.

Both halves of that are wrong. The drawing was written before it was judged. And
"shut a group or two and lay it out again" is advice the program is in a better
position to take than the person is.

Three ways out were measured and declined.

**Squeeze it.** `ColumnGap` from 108 down to 0 and `GroupPadding` from 24 down to
8: still does not fit, and not nearly. The drawing has to lose some 9600 units and
all the gap in it — 51 columns at 108 — comes to 5500. There is no version of this
that both fits and is legible.

**Make the canvas bigger.** `NodeInstance.Across` would have to go from 7500 to
about 12500. But `NodeEditor.MinZoom` is derived from the canvas width — 0.13 is
where the whole of a 15000-unit canvas fits a window about two thousand pixels
wide — so framing the far corner of a wider one needs 0.08, and a module drawn at
0.08 is sixteen pixels across. The bound is what keeps a module flung to the edge
recoverable, and buying width with it spends the thing it is there for. It is a
treadmill besides: 295 modules is already at the edge of the boxes-off drawing.

**Wrap the columns into bands.** This is the one that uses the 5000 units of spare
height, and it is the tempting one. It is declined because it puts a wire from the
end of one band to the start of the next running right to left across the whole
drawing, and 0044 is built on the signal reading left to right — in a patch the
direction *is* the meaning. Paying that to fit a drawing nobody can read at the
zoom it would need is the wrong trade.

## Decision

**A drawing too wide for the canvas has a box shut, and then another, until it
fits.** A group is one block whether it is open or shut, so shutting one changes a
width and nothing else about the drawing: the same blocks, the same links, the
same columns, in the same order. What it costs is one sub-drawing going behind a
box that is one module wide.

**Which box is a question about columns, not about boxes.** A column is as wide as
the widest block in it, so shutting a box that is not the widest in its own column
narrows the drawing by nothing at all. What is chosen is the largest reduction —
how much its column would fall to its next widest block, or to one module's width.
The difference is not academic:

| Mycelium, every box open | Boxes shut | Drawing |
|---|---|---|
| Shut the widest box in the patch | 11 of 23 | 14892 × 2454 |
| Shut the box its column narrows most for | **6 of 23** | 14444 × 4735 |

Nearly half the boxes against a quarter of them, and the cheap choice flattens the
drawing into the bottom of the canvas on the way. Where two equally wide boxes
share a column neither gains anything, and then the widest goes anyway: shutting
either makes the other worth shutting, and standing still is not on offer.

**A drawing that cannot fit even with every box shut moves nothing at all.** The
coordinates are put back and the caller is told. Which means taking them before
the first pass rather than inside one, because a pass has laid out the inside of
every group before it knows whether the whole will fit.

**Every caller gets this, not just the button.** The builder that places a preset,
the text language's binder, and the assistant's workbench all run the same
routine, and a patch that arrives from any of them arrives drawn on the canvas
rather than folded onto its edge.

## Consequences

**The button has two outcomes instead of three.** It lays the patch out, or it
leaves the patch alone and says why. There is no longer a press that half works.

**Tidy can change something that is not a coordinate.** 0044's claim was "nothing
but coordinates"; it is now "nothing the compiler reads". A box being shut is a
fact about the canvas and not about the patch — `NodeGroup` has always said so and
the compiler is never told — so the patch still compiles to the same instructions,
the sound and the picture are still untouched, and it is still one `Record` and one
Ctrl+Z. The tests that pin all of that are unchanged.

**Mycelium loses six boxes to one press, and is told which.** That is the honest
cost of a 295-module patch with everything open, and it is reported by name rather
than left to be noticed. Nothing else that ships loses one: Acid, Slow weather,
Whole band and Euclid kit all fit with every box open as they are.

**The layout may now run several times over.** At worst once per box, so a patch
with 23 of them can lay out seven times before it settles — Mycelium does. Each
pass is a few milliseconds on a patch this size, and it only happens on the patches
that do not fit the first time.

**The one case left without a remedy is a flat patch.** A patch with no boxes and
more modules than the canvas holds — some three hundred loose ones is the edge —
gets the sentence and nothing else, because there is nothing to shut. The remedy
for that is a box you *enter* rather than one that is inlined where it stands,
which would make depth cost no width at all and is a much larger piece of work
than this. Named here, not taken.
