# ADR-0042: The clock and the memory flag belong to the emitter

**Status:** Accepted · 2026-08-19 · amends
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)

## Context

[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) let a plugin hold a
memory of one evaluation, and left two things for each module doing it to work
out for itself: what rate it is running at, and what it means on a path with no
rate at all. The Filter answered both inside its own emit function — a cell
holding the previous clock, and a cell written one and read back zero where there
is no state — and 0041 recorded the shape of the problem in its own consequences:

> Nothing pins the cost of the flag to the filter. Any module reaching for the
> same trick pays another two cells and gets the same answer, and four modules
> doing it would ask the renderer for eight cells that all hold the same number.
> This is cheap enough not to matter and repetitive enough to be worth noticing
> if a third module wants it.

The Phaser is the third module. Its allpass stages need the same interval for the
same reason the Filter's integrators do, and it needs the same flag more sharply
than the Filter does: a delay line falls back to a wire on its own, and a bank of
allpass stages falls back to whatever the arithmetic happens to leave.

## Decision

**Two methods on `Emitter`: `Interval` and `HasMemory`.** The first is how far
the renderer's clock moved since the previous evaluation; the second is one where
there is a memory behind the program and zero where there is not. Both are what
the Filter already computed, moved to where the answer is the same for everybody.

**Both are emitted once per program and shared**, the way `Constant` and `Load`
already are. This is not only thrift. What they measure is a property of the
evaluation rather than of the module asking: two filters in one patch are
stepping at the same rate by definition, and a program either has state or does
not. A design where each module could answer differently would be a design where
they could disagree, and there is nothing they could sensibly disagree about.

**The interval reads the renderer's own clock, whatever domain has been pushed
over it.** A Probe substitutes its own `t` so that everything upstream is drawn
against a swept timebase ([0040](0040-a-probe-is-a-second-compile-root.md)), and
the clock a program is *stepping* at is not part of what gets swept. Making it
domain-sensitive would also make it uncacheable, since the first caller would
decide what every later one saw.

## Consequences

**The cell count is now two plus whatever the modules want.** One filter costs
four cells as it always did, two filters cost six rather than eight, and a patch
with a filter and a phaser in it costs nine rather than eleven. The tests pin
those numbers, since they are the visible form of the sharing.

**A plugin no longer has to know the trick to benefit from it.** Working out your
own sample rate by differencing a clock is not an obvious thing to think of, and
a module author who never thinks of it now gets it by calling a method that says
what it is for.

**`Emitter` grew public surface, and that surface is now load-bearing for
plugins.** It is additive, so a plugin built against an earlier `Flyback.Core`
still loads against this one; the reverse is not true, which is the ordinary
direction for a host-owned assembly ([0025](0025-platform-io-behind-loadable-plugins.md)).

**The Reverb still dims the picture.** `HasMemory` is exactly what
[0027](0027-delay-lines-give-the-audio-path-a-memory.md) lacked when it recorded
that dimming as a price rather than a choice, and rewriting the Reverb to stand
aside instead would now be three ops. It has not been done here: the Reverb's
fallback is pinned by a test that describes it as deliberate, and changing what a
shipped module does to a picture is a change to patches people already have.

**What is still each module's own is what should be.** An integrator, an allpass
stage and a feedback loop are memories *of* something, and no two modules could
share one. Only the two questions that have one answer per program moved.
