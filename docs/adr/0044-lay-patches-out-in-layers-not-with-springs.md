# ADR-0044: Lay patches out in layers, not with springs

**Status:** Accepted · 2026-08-19 · *user-directed* · finishes the placement
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) left
half-done, and shares it with the editor

## Context

A patch arriving from the assistant was cramped, and the reason was not that it
had no layout — it had one. `PatchWorkbench.Arrange` counted each node's distance
from the sink and dropped it into a column, which is the first half of the
technique this record is about. What it did not do was the second half:

- It stacked at a fixed `rowHeight = 130`, and a node is
  `26 + (inputs + outputs) × 20 + 8` tall. A Scan is 214 and the Output is 154,
  so **tall modules overlapped**.
- `columnWidth = 220` against a node width of 196 left 24 px for every wire in
  the patch to cross in.
- Nodes went into a column in the order they were built, so what crossed what was
  whatever order the model happened to place them in.

There was also no way for the person at the canvas to ask for a layout at all: a
patch dragged into a tangle stayed one.

The obvious reach is a relaxation — treat the wires as springs and let the thing
settle. It was the first idea proposed and it is the wrong shape for this graph.

## Decision

**A layered drawing, in four stages.** Cut the edges that run backwards, put
every node in a column by how far along the chain it is, order each column so
that the fewest wires cross, then place the nodes down the column so that a wire
meets its two sockets as level as it can.

**Not a relaxation, for three reasons that are about this graph rather than about
the technique.** A patch has a *direction* — everything flows into the Output —
and a spring system has no notion of one, so it settles into a blob with the sink
in the middle and wires doubling back; the usual fix is a directional bias term,
which is a layered layout arrived at badly and with iteration. It would not be
*repeatable*: the same patch would relax differently from different starting
positions, and a button that reshuffles the canvas every press destroys the map
the user has built of their own patch. And what was actually being asked for was
*fewer crossings*, which the ordering sweep attacks directly and which springs
only ever reduce by accident.

Nothing here is a discovery about the aesthetic, either: the presets in
`Presets.cs` were hand-placed at x = 40, 300, 540, 800, 1080, 1320. The target
was already columns.

**The back edges are given rather than guessed.** Every other layered drawing has
to choose a set of edges to reverse and lives with the choice. This one is told:
[0012](0012-feedback-as-a-module-not-a-cycle.md) and `Patch.WouldCycle` between
them mean a cycle can only pass through a module marked `IsCycleBreaker`, so
dropping the wires that *leave* one leaves a graph acyclic by construction — and
drops them where the meaning already was, since what leaves a Unit Delay is the
previous evaluation and was never part of this one's chain.

**The column is the longest path forward, and the Output is pinned to the last
one.** Longest rather than shortest because a wire must never run backwards.
Pinned because the sink is the end of the patch and reads as the end, while the
arithmetic alone would put a Probe hanging off a long chain further right than
the Output it sits beside.

**Down a column, a node is placed where its wires arrive level.** The sockets are
at known offsets, so a node can be put where its input port lines up with the
output port feeding it — a straight wire instead of a diagonal. Averaged over the
wires a node has, since one with three inputs cannot line up with all three.

**It lives in the engine and is handed the editor's dimensions.** Two callers
want it and only one has a canvas. `PatchLayout.Metrics` is how the sizes travel,
so the engine never learns what a node looks like — and a test in the shell pins
the two together, because nothing in the type system does and a layout run
against the wrong height is exactly the overlap this record is about.

**Both callers use it.** The toolbar button, and the workbench — so a patch that
arrives from the assistant is laid out exactly as one the user has just tidied,
and there is nothing to clean up afterwards. That was the complaint that started
this.

## Consequences

**The button is one edit and changes nothing but coordinates.** No wire is added,
removed or rerouted, so the patch compiles to the same instructions before and
after and neither the picture nor the sound moves. One `Record`, so one Ctrl+Z
puts every node back at once. Both are pinned by tests, because a tidy button
that quietly altered a patch would be the least forgivable kind of bug here.

**It is repeatable but not position-independent.** The ordering starts from where
the nodes already are, so a patch that is nearly right is tidied rather than
rearranged and a module dragged to the top stays near the top. The same patch in
the same positions lays out the same way every time, and laying out an
already-laid-out patch changes nothing — which is the property being claimed over
a relaxation, and it is the weaker of the two readings of "deterministic". The
stronger one was not worth losing the user's arrangement for.

**A patch of repeated voices lays out worse than a person would do it.** The
"Four voices" preset is four identical chains, and the hand-placed version is a
four-by-four grid 470 px tall. Laid out here it is one column of twelve
oscillators, some 2300 px tall — correct, legible, and much larger. Nothing in a
layered drawing can see that four subgraphs are the same shape and belong in
rows; finding that would be a different and much larger piece of work. The button
helps most on the tangle it was written for and is a poor trade on a patch
already arranged by hand, which is the right way round.

**A column can be very tall where a node fans in widely.** Twelve oscillators
into one Mixer is twelve nodes at one x, and the modules reading them individually
end up at the extremes of the next column with long wires back. This is honest
rather than avoidable: they really are all one step along the chain.

**A module whose plugin is not installed is left exactly where the file put it.**
It cannot be measured, and moving it to a guessed size would scatter the one
thing a patch from a missing plugin still has going for it. A module wired to
nothing goes in a column of its own before the first, rather than among the
sources it is not one of.

**The tidy icon is drawn rather than typed.** No character means "lay this out",
and the ones that come close resolve to the colour emoji font on Windows — the
same finding `Glyphs` already recorded for the folder and the floppy disk. It is
a patch in miniature: two modules feeding one.
