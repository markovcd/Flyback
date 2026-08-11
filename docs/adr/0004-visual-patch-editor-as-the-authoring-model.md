# ADR-0004: Author patches in a visual node editor

**Status:** Accepted (user-directed) · 2026-08-11

## Context

The brief drew an explicit analogy to audio synthesis. That analogy admits
several authoring models, and the choice determines where most of the work goes.

Three options were put to the project owner:

1. **Composable C# signal graph** — `Sin(X * 10 + T) * Osc(0.5)`. Type-safe,
   IntelliSense, unit-testable, serialisable. Smallest UI surface.
2. **Visual patch editor** — drag modules onto a canvas and wire them up, like a
   modular rig. Most fun to use; most of the work becomes UI rather than synthesis.
3. **Text DSL / live-coded expressions** — parse and hot-reload expressions, in
   the style of Hydra or TidalCycles.

The owner chose option 2, having been told option 2 shifts effort into UI.

## Decision

A visual node graph is the primary authoring surface. Patches are graphs of
placed modules and wires, edited on a canvas, with a live preview beside it.

## Consequences

This is the single most expensive decision in the project and it landed exactly
where predicted: `NodeEditor.cs` is 477 lines, the largest file in the solution,
and the shell as a whole is slightly larger than the engine it drives.

It also forced several downstream decisions that turned out well.
Because a graph must be reduced to something evaluable, the compiler
([0005](0005-compile-to-a-flat-register-machine.md)) exists as a distinct stage
rather than being fused into evaluation — which is what leaves room for a GPU
backend. Because knob values live on nodes rather than in code, ports carry
editable defaults ([0009](0009-editable-defaults-on-every-input.md)). Because
modules must be discoverable in a palette, they are declared as data
([0008](0008-modules-as-data-in-one-catalogue.md)).

The option not taken is not foreclosed. `PatchBuilder` gives a fluent C# API
over the same graph, and the presets are written with it — so patches *can* be
built in code, they just are not the primary route. A text DSL would be a parser
producing a `Patch`, which is a self-contained addition.

The main risk is that a graph editor has a long tail of interaction polish —
box selection, undo, copy/paste, node grouping, wire routing — none of which is
implemented. What exists is enough to patch and re-patch, and nothing more.
