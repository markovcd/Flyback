# ADR-0069: A box is written back by sorting the statements again

**Status:** Accepted · 2026-09-02 · *user-directed* · closes the open loop
[0065](0065-a-text-language-that-parses-to-a-patch.md) left; implemented in
`Language/PatchPrinter.cs`; rests on
[0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)

## Context

[0065](0065-a-text-language-that-parses-to-a-patch.md) gave the printer one
documented hole and said plainly that it was not fixed:

> Groups are the exception because of ordering: a binding is written the moment
> something first needs it, and a group's members are not contiguous in that
> order, so a group would have to be opened and closed and opened again.

The diagnosis was right. Whole band's `Clock` is four modules and half the patch
reaches for one of them, so emit-on-first-use spreads them from one end of the
printing to the other. A block cannot be drawn round a run of statements that is
not a run.

The cost was not small either. A group is presentation — the compiler is never
told about one — but Whole band uses ten of them to organise ninety-two modules,
and 0065's own reference says a text form that dropped them would be unreadable
at that size. It is also the one thing standing between the printer and being a
real round trip: everything else survives, and the code view
([0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md)) had to
carry a notice counting the boxes a printing would cost.

Two lesser problems sat underneath it, and neither is visible until the first is
solved:

**A module folded into a pipeline is declared wherever that pipeline is.** The
printer inlines anything used once, and an inlined member would land outside the
block whatever the block did.

**A bare word is a reading rather than a declaration.** `t` and `x` are the
clock and the coordinates, written as words because there is one of each. A
module declared nowhere cannot be inside a block — and worse, the binder
materialises one at its first mention, so a rebuild would sweep the whole
patch's clock into whichever box happened to name it first.

## Decision

**The order is worked out again, with each group counted as one thing.** Every
statement is a unit, a group is a unit of several, and a unit may be written
once everything it names has been. That is an ordinary topological sort over
units instead of over statements, and it finds an order in which every group is
contiguous whenever one exists — which the emit order does not, because it was
never looking for one.

**Ties go to whichever statement was written first**, so the result stays as
close to the reading order as the boxes allow. The point of emit-on-first-use is
that it reads well, and this keeps as much of it as it can.

**The printer records what each statement names.** A statement writes the
statements it needs as it goes, so what a statement reached for is kept on a
stack: the entry on top is always the one being written, and a nested one takes
its own. Without that there is no dependency graph to sort.

**Anything in a box gets a `let`, whatever its shape.** The rule that inlines a
module used once is the right rule and stays; it simply does not apply to a
module that has to be declared somewhere in particular.

**A clock or a coordinate pair inside a box is written as the module it is.**
`time()` and `coord()` rather than `t` and `x`, which costs a word and keeps the
box. Outside a box nothing changes, which is nearly always.

**And the binder never lets a block adopt one.** There is one clock in a patch
and every line saying `t` is reading it rather than making it, so which block
mentions it first is an accident — and one that would put the whole patch's
clock in somebody's box.

**A box keeps its identity across a rebuild**, named after the block that drew
it, the way a module is named after the piece of source that made it
([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)).
Without it the canvas would forget which box was open on every evaluation, which
is the same failure 0067 exists to prevent one module at a time.

**Where two boxes reach into each other, a box gives way rather than the
patch.** No order stands them both together; the widest is dropped and the rest
tried again, which terminates because there is one fewer each time. Presentation
is worth less than a text that reads back — and a pair of boxes each reaching
through the other is a shape nothing on a canvas has reason to be, though it can
be drawn.

## Consequences

**All ten of Whole band's boxes survive a round trip, with every module in the
one it was in.** Before this, six survived when the members happened to be
contiguous and four did not. The test is over every preset that has a box in it,
comparing membership by name.

**Printings of grouped patches have more bindings in them.** A boxed module that
would have been folded into a pipeline is now a `let` of its own. It reads
about as well and diffs better, and it is the price of membership meaning
anything.

**`Patch.Group` takes an id**, as `NodeInstance.Create` and `Patch.EnsureOutput`
already did, and for the same reason: a caller that supplies one is saying this
is the same box as before. Nothing but the binder supplies one.

**What is still lost is what was always editor state**: whether a box is
collapsed, and which of its sockets are exposed. Both are how somebody was
looking at a patch rather than what the patch is, and neither has a place in the
language to be written to.

**The reference and the shell both said groups were dropped**, in four places
between them — a save's report, the code view's notice, and two paragraphs of
prose. All four are now wrong in the other direction and were corrected with
this, which is the part of a fix like this that is easy to leave undone.
