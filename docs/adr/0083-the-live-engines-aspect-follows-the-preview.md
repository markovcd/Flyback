# ADR-0083: The live engine's aspect follows the preview

**Status:** Accepted · 2026-09-17 · *user-directed* · amends
[0077](0077-the-picture-is-heard-only-through-a-scan.md)'s aspect table, whose
Live engine row fixed the number at 16:9

## Context

[0077](0077-the-picture-is-heard-only-through-a-scan.md) gave `AudioRenderer` an
`Aspect` a patch reads through Coordinates' `aspect`, and settled the live
engine's at a fixed 16:9 — "the sound export's shape, so a patch sounds the same
played as written." That export was the shell's, and it wrote at a fixed 16:9
of its own. Fixing the two together was consistent: whichever shape a patch was
judged by, live or written, was the same shape.

The size list in the settings window ([0082](0082-the-output-settings-move-to-the-settings-window.md))
offered only 16:9 rows until now, so the live engine's fixed 16:9 always
happened to match whatever the preview showed. Widening that list to sizes of
other shapes — 4:3, a square, a phone's portrait — breaks the coincidence.
A patch with a Scan reading `aspect` to reach the picture's edges, of the kind
[0077](0077-the-picture-is-heard-only-through-a-scan.md) gives as its example,
would go on hearing 16:9's edges while looking at a different rectangle: the
sweep would fall short of one pair of edges or run past them, on a picture
that no longer has the shape the sound was told it did.

## Decision

**`AudioRenderer.Aspect` is settable, not fixed at construction.** Every other
renderer still sets it once — an export, a render or the assistant's listen
each answer for one frame that does not change shape mid-render. The live
engine's does, whenever a person picks a different size, so its setter has to
outlive its constructor.

**`AudioEngine` gains an `Aspect` property** that reaches the renderer
directly. `MainWindow.UseOutputSettings` — the one place a chosen size reaches
the preview (0082) — sets it alongside `preview.Resolution`, from the same
width and height, so the picture and the live sound agree on a shape by
construction rather than by the size list happening to hold one shape only.

**The row in 0077's table now reads "the preview's shape, kept in step as it is
changed."** The rule that a patch sounds the same played as written no longer
follows from a shared constant; it follows from both being asked with the same
width and height, which is closer to what the rule meant in the first place.

## Consequences

**A patch saved before this reads `aspect` differently live only if its
preview is not 16:9.** At the shape settings had always offered until now,
nothing changes. Nothing changes for an export or `flyback-cli render` either
— [0077](0077-the-picture-is-heard-only-through-a-scan.md) already read each of
those from the frame being written, not from a constant.

**Setting it costs nothing on the audio thread.** The property is read once a
buffer, inside `AudioRenderer.Render`; the window writes it from the UI thread
on Save, the same moment it writes `preview.Resolution` and hands the
compiler its switch. A buffer already in flight finishes with the aspect it
started with, the same as every other setting `UseOutputSettings` hands out.
