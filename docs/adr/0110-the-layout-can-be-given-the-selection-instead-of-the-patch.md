# ADR-0110: The layout can be given the selection instead of the patch

**Status:** Accepted · 2026-09-20 · *user-directed* · narrows what
[0044](0044-lay-patches-out-in-layers-not-with-springs.md) is run over · keeps
[0092](0092-a-drawing-too-wide-for-the-canvas-shuts-a-box.md)'s two outcomes

## Context

Laying a patch out was all or nothing. One press moved all 295 of Mycelium's
modules, and the drawing landed in the middle of the canvas whatever had been
where. On a small patch that is exactly what is wanted. On a big one that has been
arranged by hand over an hour, with one corner newly wired and tangled, it is a
button nobody presses: the corner would come out tidy and everything else would be
thrown away with it.

What is wanted there is the layout run over six modules. `PatchLayout` was already
most of the way to it. A pass is built from a `defs` dictionary of the modules it
will place, and it has always placed fewer than all of them — a module whose plugin
is missing cannot be measured, so it is left out and left where it is. Handing that
same dictionary a smaller set is the whole of the mechanism.

Two things stood in the way of it, and neither is about the layered placement.

`Settle` centred the finished drawing on the canvas. For a whole patch that is the
only choice that uses the whole canvas. For six modules out of 295 it is the wrong
one twice over: the six land in the middle of a patch they have no business being
in the middle of, and the view the person was working through is now looking at
somebody else's modules.

And a group. The placement moves a group as one rigid block and never as its
modules, because a box is drawn from where all of its members are — so a selection
that reaches into a box halfway is a question the placement cannot answer.

## Decision

**`Arrange` takes the modules to place, or null for all of them.** One parameter,
and the pass filters `defs` by it exactly where it already filters by what the
catalogue knows. Everything below that — the cut edges, the columns, the ordering,
the group interiors, the box-shutting retry — is untouched and does not know the
difference. Which is the point of putting it there: laying out part of a patch
produces a drawing built by the same rules as laying out all of it, because it *is*
the same code.

**The drawing lands on the middle of where its own modules were.** `Settle` is
given the point to land on rather than assuming the origin, and a whole-patch
layout passes the middle of the canvas, which is what it always did. A selection
passes the middle of the box its modules were in, read before the first pass
because a pass moves the modules it would be measured from. A drawing that would
then hang over an edge is slid back inside, since a coordinate clamps as it is
written and a drawing left hanging would stack against the boundary — the same
failure [0092](0092-a-drawing-too-wide-for-the-canvas-shuts-a-box.md) was written
for.

**A box with a module outside the set in it is left alone whole.** The rule the
missing-plugin case already had, reread: a group is placed only when every one of
its members is being placed. So a selection that is exactly a box lays that box out
among itself, and a selection of half a box moves nothing of it.

**Two ways in, and both are the narrower reading of one thing that was already
there.** Ctrl+Shift+L beside Ctrl+L, where Shift already means the narrower or the
opposite of a key throughout — Ctrl+Shift+Z, Ctrl+Shift+G, Ctrl+Shift+E. And Ctrl
held on the toolbar's layout button, which needs the modifier read off the press on
its way in, because a `Click` says which button was pressed and nothing about what
was held down. The tip says so, since a modifier nobody can see is a modifier
nobody finds.

**With nothing selected it says so and moves nothing.** The alternative was to
treat an empty selection as everything, which would make the two keys the same key
at the moment they differ most.

**The canvas keeps its view.** No framing afterwards, because the selection went
back where it was and there is nothing to bring into sight. Moving the view would
take away the part being worked on, which is the whole reason the narrower press
exists.

## Consequences

**Something that was not selected may end up underneath something that was.** The
layout is given six modules and the rest of the patch is deliberately not consulted
— asking it to also avoid 289 others is asking for the whole-patch layout again,
with a worse drawing. What is placed is what was asked for, and the overlap is a
drag away. This is the honest cost of the feature and not a defect to be designed
out.

**Ctrl+A then Ctrl+Shift+L is not Ctrl+L.** It lays out every module, but leaves
the drawing where the patch already was instead of centring it on the canvas. That
follows from the rule rather than contradicting it: the narrower press is the one
that does not move the view.

**The box-shutting retry can fire on a selection.** It is the same loop, so a
selection of open boxes too wide to draw has boxes shut until it fits, and says
which — with "selection" where the sentence said "patch". A selection that cannot
fit at all moves nothing, as a patch that cannot does.

**`Arrange`'s signature changed rather than gaining an overload.** The public-API
analyzer requires the overload carrying optional parameters to be the one with the
most of them, so a second entry point was not available. The old signature is
marked removed in `PublicAPI.Unshipped.txt`; nothing outside the app called it.

**Laying out only part of a patch is still one edit.** One `Record`, one Ctrl+Z,
and nothing the compiler reads has changed — 0044's and 0092's claims hold
unaltered, because what is different here is only how many modules the pass was
handed.
