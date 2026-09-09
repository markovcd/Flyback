# ADR-0070: A preset declares no coordinates

**Status:** Accepted · 2026-09-09 · *user-directed* · implemented in
`Graph/PatchBuilder.cs` and in every preset the engine and the plugins ship;
finishes what [0044](0044-lay-patches-out-in-layers-not-with-springs.md) started
by making the layout the only thing that places a shipped patch; puts the
presets where [0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) already
were

## Context

Every shipped patch placed itself. `b.Add("osc.sine", 940, 300, …)` — a type id,
two coordinates, then the knobs — some five hundred and sixty times across the
thirty presets in the box.

Those numbers were the last hand-placed coordinates in the program, and by the
time [0044](0044-lay-patches-out-in-layers-not-with-springs.md) was written they
were already imitating what the layout does: that record notes the presets were
hand-placed at x = 40, 300, 540, 800, 1080, 1320 and that "the target was already
columns". The two other ways a patch is authored had stopped saying where
anything sits — the text language has no coordinates in it at all and lays out on
build ([0065](0065-a-text-language-that-parses-to-a-patch.md)), and the
assistant's workbench places a patch for a model that never thinks about a canvas
([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)).

What the numbers cost is not the typing. It is that they are a second, weaker
statement of a thing the program computes: they go stale the moment a module is
added to the middle of a preset, they have to be renumbered by hand down a whole
column when one does, and they say nothing about *why* a module is where it is —
where the layout can at least answer that it is one step further along the chain.
A preset that had drifted also disagreed with the button: pressing Tidy on a
patch just picked would shuffle it, which reads as the program overruling the
author rather than as the author having left work for it.

## Decision

**`PatchBuilder.Add` takes a type id and knobs.** A preset says what modules are
in the patch, what their knobs are, what is wired to what, and which of them
group together. Where they sit is not one of those things.

**`PatchBuilder.Build` places the patch and hands it over**, running the same
`PatchLayout.Arrange` the Tidy button and the binder use. Every preset now ends
`return b.Build();` where it ended `return b.Patch;`.

**At the builder rather than at the picker.** Four routes reach a preset — the
app, the command line, a test and a benchmark — and placing it at the one place
they all pass through is what makes "a preset arrives placed" true of the patch
rather than true of the app.

**The placement is not an edit.** It happens as the patch is built, which is
before it reaches `NodeEditor.Patch` and therefore before the history opens on
it. So Ctrl+Z after picking a preset has nothing of the layout to take back —
the same treatment, and for the same reason, that `EnsureOutput` and
`HoldInside` already get at that setter: they are the gates a patch passes on
the way to being shown, not things somebody did to it.

**The coordinate overload stays, for callers that mean a coordinate.** A test
about dragging, marqueeing, framing or hit-testing is a test in which where a
module sits is the subject, and it says so by placing modules itself and taking
the patch from `b.Patch`, which leaves them where they were put. The two exits
are the whole of the distinction: place it yourself and take `Patch`, or do not
and take `Build`.

## Consequences

**A preset reads as what it is.** The declarations are shorter by two numbers
each, and what is left in them is the instrument.

**The order a preset is written in is the order it is drawn in.** The layout
sorts each column by where the nodes already are, and with nothing placed that
tie is broken by the order they were built — so a column comes out top to bottom
in the order the source declares it. This is worth knowing when writing one: the
declarations are still an arrangement, just of the only axis a preset now
controls.

**The shipped patches look different, and one of them looks worse.** The cost
[0044](0044-lay-patches-out-in-layers-not-with-springs.md) recorded is now paid
in the box rather than only on the button: nothing in a layered drawing can see
that four voices are the same shape and belong in rows, so "Four voices" is one
column of oscillators 1800 units tall where the hand-placed version was a grid.
"Whole band" is 92 modules across 5060 units. Both are correct and legible and
neither is what a person would have drawn.

**The largest preset now spans about half the canvas.** `NodeInstance.Extent`
is ±5000 on each axis and the layout starts at x = 40, so "Whole band" ends some
100 units short of the right edge. Nothing is clamped, and a preset appreciably
larger than that one would be — which is a real limit on how big a shipped patch
can be, and better as an edge somebody meets when writing one than as a number
nobody was checking against by hand.

**Laying out an arrived preset can still move something a few pixels.** The
ordering sweeps are a fixed number of passes rather than a search for a fixed
point, so starting them from the layout's own answer can settle a node
differently than starting them from a pile at the origin — a 4-pixel move on one
module of "Whole band". So what the tests pin is that a preset arrives with
nothing overlapping, which is the property being claimed, rather than that
arranging it again is exactly a no-op, which the layout has never promised from
an arbitrary starting position.

**A plugin's preset that returns `b.Patch` hands over a pile at the origin.**
This is the one new way to get a preset wrong, so it is guarded by a test over
every preset in the picker and written down in the plugin guide beside the
example. Nothing else changes for a plugin: the coordinate overload is still
there, so one compiled against an earlier build keeps working.
