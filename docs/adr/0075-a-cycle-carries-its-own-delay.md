# ADR-0075: A cycle carries its own delay

**Status:** Accepted · 2026-09-16 · *user-directed* · implemented in
`Graph/Cycles.cs`, `Compile/PatchCompiler.cs`, `Graph/PatchIO.cs`,
`Language/PatchPrinter.cs` and `Controls/NodeEditor.Painting.cs` · supersedes
[0012](0012-feedback-as-a-module-not-a-cycle.md)'s ruling that a cycle in the
graph is an error, and removes the module
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) introduced ·
rests on [0074](0074-a-cell-is-a-plane-on-the-video-path.md), which is what
makes a loop mean the same thing to both sinks

## Context

A loop was two things: the wires, and a Unit Delay somewhere among them. The
module was where the previous evaluation came from, and the compiler refused any
cycle that did not pass through one. The canvas softened that by putting the
module on the wire for you — draw the cycle, and the delay it needs arrives with
it — so the gesture was already "draw the loop".

What was left was the module itself, and what it cost was hard to defend once the
gesture existed:

**It says nothing the wire does not.** A Unit Delay has one input, one output, no
knob, and no choice to make. Every one of them in every patch means the same
thing, and that thing is "this loop is delayed by one evaluation" — which is true
of every loop that compiles at all.

**It was in the way of everything that reads a patch.** The compiler, the layout,
the printer and the canvas each asked "is this module a cycle breaker", which is a
question about a *wire* asked of a node. The layout cut what left a breaker, the
printer wrote a breaker as `unit()` and its input as a back-wire, and each of them
carried its own version of the rule.

[0012](0012-feedback-as-a-module-not-a-cycle.md) had one real argument for the
module, and it was about the picture: a cycle would have to be *silently
reinterpreted* as a delay, and where that reinterpretation happened would be
invisible. [0074](0074-a-cell-is-a-plane-on-the-video-path.md) answered the first
half — a cycle now means one frame at this pixel, which is a thing a picture can
do — and left the second. This record answers the second by making the wire
visible rather than by adding a module to stand for it.

## Decision

**The wire that closes a loop carries the previous evaluation, and is drawn
dashed.** There is no module. A cycle is legal, and what makes it legal is
something you can see on the canvas.

**One walk decides which wire that is, and everything asks it.** `Cycles.Backwards`
is a depth-first walk in the order the patch is written down — nodes as they are
listed, wires as they were drawn — and a wire into a node the walk is still
inside is the one that runs backwards. The compiler reads a plane there, the
canvas dashes it, the layout leaves it out of the layers, and the printer writes
it as the back-wire the language already had. Four readings of a patch, one
answer.

**Order is what makes it stable.** A file lists its nodes and wires in a fixed
order and reads back in the same one, so a patch opened twice cuts its loop in
the same place. Rerouting a wire may move the cut, which is the honest cost of
not having a module to pin it to: nothing about a ring of wires distinguishes one
of them until something looks.

**The plane belongs to the wire.** `Cycles.Owner` hashes the two ends into a
name, the way [0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)
hashes a source path into one, so an edit elsewhere in the patch leaves a running
loop carrying what it was carrying. Moving either end of that wire is a different
loop, and it starts again from nothing.

**One output closing two loops is one plane.** What is delayed is a value, not a
wire, so two backward wires from the same socket read one plane rather than
keeping the same number in two places.

**A module may be wired to itself.** That is a loop of one, and there was never
anything wrong with it beyond its being the shortest cycle — `Patch.Connect`
refused it from the first commit, when every cycle was an error. The canvas
slings such a wire under the module rather than straight across it, because
resting wires are drawn beneath the modules and a loop of one drawn flat would
be hidden by the box it belongs to.

**Every read still lands before every write.** The compiler resolves the whole
program, then drains the loops — and the writes are emitted in one pass after
that draining is finished, because resolving one loop can reach another and a
write emitted too early would let the second loop read this evaluation instead of
the one before.

**The patches migrated are the ones written in C#.** The Loop preset draws its
ring directly, and the language's own examples with it. A saved `.fbk` holding a
Unit Delay is not rewritten on the way in: the file format does not move, and the
module comes back through the path any missing module takes — named in
`PatchLoad.UnknownModules`, compiled as silence, and left on the canvas to be
taken out by hand. Writing a migration for it would be a second implementation of
"what a loop means", kept for files that can be fixed with one delete and one
wire.

## Consequences

**Drawing a loop is drawing a loop.** No module arrives, nothing is selected,
nothing has to be tidied away afterwards, and undo takes back exactly the wire
that was drawn.

**A dashed wire is a thing to learn.** It is the only mark on the canvas that
means "this carries the evaluation before", and somebody who has not met it will
read a dashed wire as a wire. That is the price of the module going: the
explanation used to sit in a panel, and now it sits in a line style and in the
handbook.

**The compiler has one refusal fewer.** "Feeds back into itself" is no longer
reachable from a patch — every loop is cut before the walk starts — and the check
that raised it stays only as a guard for a program assembled by hand whose wires
disagree with the walk. Two of the assistant's tests and one scenario had used a
cycle as their example of a fault, and now use a clip naming a file that is not
there.

**Two evaluations of delay are no longer expressible.** Chaining two Unit Delays
used to buy two, and nothing replaces it: a loop is one evaluation, and more than
that is a Delay module for the ear and nothing at all for the eye. Nothing in the
box used it, which is why this is a line here rather than a plan.

**Where a loop is cut is no longer yours to choose.** With a module you could put
the delay on a particular wire; now the walk chooses. Within one loop the choice
cannot be heard — the ring is delayed by one evaluation wherever it is cut — but
it decides which module reads a stale input when several loops share a module, and
it decides which wire is drawn dashed.

**`NodeDef.IsCycleBreaker` and `Patch.WouldCycle` are gone.** A plugin cannot
declare a breaker any more, and nothing asks whether a wire would close a loop,
because the answer no longer changes what happens.

**An old patch says what is wrong with it rather than opening changed.** A file
with a `feedback.unit` in it lists that module as one this build does not have,
which is the same sentence any patch from a missing plugin gets — and unlike a
silent rewrite, it leaves the patch as its author saved it until somebody decides
what to do with it.
