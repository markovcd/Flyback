# ADR-0009: Every input port carries an editable default

**Status:** Accepted · 2026-08-11

## Context

Most inputs in a real patch are constants. An oscillator's frequency is 1.5, a
kaleidoscope has 6 segments, a gain is 0.95. Only a minority of inputs are
actually modulated by another module.

On a hardware modular synth those constants are knobs on the panel. In a naive
node editor they are *nodes* — a "Constant" module wired into every input that
is not otherwise driven. The Plasma preset would need eight of them, and the
canvas would be mostly constants.

## Decision

Every input port declares a default, and every placed node stores an editable
value per input:

```csharp
public readonly record struct PortSpec(
    string Name, PortKind Kind = PortKind.Scalar,
    float Default = 0f, float Min = -4f, float Max = 4f);
```

`NodeInstance.InputValues` is seeded from those defaults. The compiler uses the
stored value when nothing is wired in:

```csharp
value = incoming is not null ? /* upstream slot */ : emitter.Constant(DefaultFor(node, port, spec));
```

`Min`/`Max` are slider hints for the inspector, not clamps on the value.

## Consequences

Patches are small. The Plasma preset is 8 modules and 8 wires; with constant
nodes it would be roughly twice that, and the interesting structure would be
buried.

The node itself shows its knob values — the editor draws the number on the right
of any unconnected input row, and hides it the moment something is patched in.
The visual distinction between "this is set to 1.5" and "this is driven by
something" is the presence of a wire, which is how a patch bay reads.

Wiring into an input silently overrides its knob rather than erasing it. Unplug
and the previous value returns, because it was never lost. This makes
experimentation cheap and is the reason dragging a connected input away
([0017](0017-draw-the-node-editor-in-one-control.md)) is a safe gesture.

`Emitter.Constant` de-duplicates by value, so eight inputs defaulting to `0`
share one register and one `Const` op.

The hardware metaphor later became literal. `PortSpec.NormalledFrom` names an
earlier input to fall back to instead of a constant, which is exactly what a
normalled jack does on a real rig — leaving the Audio Output's `right` unpatched
carries `left` through, so a mono patch is stereo without saying so
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)).

Two costs. `InputValues` is a `float[]` positionally aligned with the
definition's inputs, so reordering a module's ports silently reassigns saved
values — see [0020](0020-json-patch-files-keyed-by-string-type-ids.md), which
handles length changes but not reordering. And there is no way to give a colour
port a default: `PortSpec.Default` is a single float, so an unwired colour input
compiles to a broadcast grey. In practice colour inputs are essentially always
patched, so this has not bitten.
