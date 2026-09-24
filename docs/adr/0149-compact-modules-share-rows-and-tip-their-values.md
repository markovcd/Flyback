# ADR-0149: Compact modules share rows and put their values in the tooltip

**Status:** Accepted · 2026-09-24 · *user-directed* · amends
[0009](0009-editable-defaults-on-every-input.md)

## Context

A module is drawn with every output above every input, one row each, and an
unwired input writes its value at the right of its row (0009). A Filter is
seven rows, and a patch of forty modules is mostly rows. The values are what
make a row wide enough to need a whole module's width.

## Decision

**Settings → Canvas → Compact modules** draws a module with input *i* and output
*i* on one row, input on the left and output on the right, so it is as tall as
its longer side. Nothing is written after an input's name: what it rests at
(its value, the panel knob it follows, or what it is normalled from) is the first
line of the tooltip on its socket and on its half of the row, above the socket's
help. A shut box follows the same rule.

The switch is off by default, kept in `canvas.json`, and has no shortcut, since
it is meant to be chosen once rather than flipped while patching.

**Tidy spaces modules at the size they are drawn.** `PatchLayout.Metrics` gains
`SharedRows`, which the canvas sets. A patch tidied compact can overlap once the
switch is cleared. Presets, the text language and the assistant's workbench have
no canvas and keep the full size, which is never shorter than compact.

## Consequences

A value is a hover away rather than a glance away, which the switch leaves as
the person's trade. A panel knob's link shows as a dot after the input's name,
and an Auto remap range with no range at the far end is flagged on the name.

`NodeGeometry.Compact` is one static switch for the whole program, like
`ModuleSkins.Honored`. A test that turns it on runs on the UI thread and turns it
off again.
