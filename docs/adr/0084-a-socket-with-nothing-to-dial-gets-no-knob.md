# ADR-0084: A socket with nothing to dial gets no knob

**Status:** Accepted · 2026-09-17 · *user-directed* · amends
[0009](0009-editable-defaults-on-every-input.md); the Output's `color`, `left`
and `right`, whose knobs this retires two of, were declared by
[0037](0037-one-output-block-that-every-patch-has.md)

## Context

[0009](0009-editable-defaults-on-every-input.md) gave every input an editable
default and a slider, on the theory that most inputs in a real patch are
constants. That ADR already named the one case where the theory does not
hold, in its own consequences: *"there is no way to give a color port a
default: `PortSpec.Default` is a single float, so an unwired color input
compiles to a broadcast grey. In practice color inputs are essentially
always patched, so this has not bitten."* It has not bitten because nobody
drags the slider, not because the slider does anything reasonable — a single
brightness knob standing in for a value one float cannot hold. Eleven color
inputs carry it: the Output's own `color`, `color.split`, both halves of
`color.mix`, `color.gain`, and Grade, HSV, Layer (`base` and `top`), Palette
and Posterise in the Picture plugin.

The Output's `left` has the same symptom for a different reason. It is
Scalar, so `Default` can hold it, but there is nothing to dial: a constant
fed to a speaker is a DC hum, not a setting, and every preset in
`Presets.cs` patches it rather than leaving it on its knob. `right` already
falls back to `left` when nothing is patched in —
[0037](0037-one-output-block-that-every-patch-has.md)'s `NormalledFrom` —
so its knob was dead twice over: turning it did nothing unless a wire made
the knob irrelevant a second way. The inspector never said so, because
`BuildInputRow` special-cased `NormalledTo` (a hidden module standing in for
a wire, named in the row as "◀ Time, without a wire") but had no branch for
`NormalledFrom` (an earlier input doing the same job) or for a color's
structural inability to hold a knob at all.

## Decision

**`PortSpec.PatchOnly`**, a new flag beside `Domain` and `Swept`: true when
`Default` is filler for the compiler rather than a setting for a person.
**`PortSpec.NeedsAWire`** is `PatchOnly || Kind == PortKind.Color` — every
color input qualifies on its kind alone, so none of the eleven needs a
change at its own declaration.

The Output's `left` is marked `PatchOnly` explicitly, being Scalar. `right`
needed no new flag: `BuildInputRow` gained a branch for `NormalledFrom`
alongside its existing one for `NormalledTo`, so an earlier-input fallback
is named in its row exactly as a hidden-module one already was — "◀ left,
without a wire". A socket where `NeedsAWire` is true and nothing is patched
gets a plainer row for the reason a `NormalledTo` row has no slider: nothing
a drag could set would be read back, so it says "◀ not patched" instead of
drawing a control that would do nothing.

`volume` is untouched. [0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md)
already established it as a real dial down to off, so it stays the one
Output socket with a slider.

## Consequences

The Output's panel offers one knob instead of four: `volume` alone, with
`color`, `left` and `right` each named for what they need instead of a
slider nobody could use correctly. Grade, HSV, Layer, Palette, Posterise and
the built-in `color.split`/`color.mix`/`color.gain` lose the same dead
slider for free, since the rule reads off `Kind` rather than a per-port
declaration.

Nothing about compilation changes. The stored `Default` still answers
`DefaultFor` exactly as before, so an unwired `PatchOnly` or color socket
compiles to the same constant it always did — this is the inspector reading
a fact that was already there, not a new one.

A future color-kind port inherits `NeedsAWire` for nothing. A future Scalar
port that is signal-only the way `left` is has to say so with
`PatchOnly = true`, the same as `left` does: there is no way yet to infer
it from a port's shape, and inferring it wrong would hide a knob someone
wanted.
