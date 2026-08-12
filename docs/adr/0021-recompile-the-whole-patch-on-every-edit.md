# ADR-0021: Recompile the whole patch on every edit

**Status:** Accepted · 2026-08-11

## Context

Every edit changes the program: dragging a wire, deleting a module, and — most
frequently — dragging an inspector slider, which fires continuously through the
gesture.

The instinct is that recompiling a graph on every mouse-move sample is wasteful,
and that knob changes in particular should be special-cased. A knob only changes
a `Const` op's `K` value; patching it through as a constant update would avoid
compiling at all.

## Decision

Recompile the entire patch on any change. One event, one handler:

```csharp
editor.PatchChanged += (_, _) => Recompile();
```

`Recompile` calls `editor.Patch.CompileForVideo()`, assigns the result to
`preview.Program`, and puts any issues in the status bar. There is no
incremental path and no special case for constants.

## Consequences

Compilation is far cheaper than the thing it feeds. The Plasma preset compiles to
33 ops over 35 registers — a graph walk over 8 nodes producing two small arrays,
microseconds of work. The frame it produces costs 3.5 ms. Even at slider sample
rates, compilation does not register against rendering.

There is exactly one path from patch to program, so a knob drag and a fresh file
load go through identical code. Nothing can be stale, because nothing is
retained — the class of bug where a patch edit fails to invalidate a cached
subtree cannot occur.

It composes with threading rather than fighting it. Each compile produces a fresh
immutable `CompiledPatch`; the background renderer captured the previous one and
finishes with it safely, and the next tick picks up the new one
([0018](0018-never-render-frames-on-the-ui-thread.md)). Incremental compilation
would mean mutating a program a background thread might be executing, which needs
either locking or a copy — at which point the copy is the recompile.

Constant folding, dead-code elimination
([0011](0011-compile-backwards-from-output.md)) and common-subexpression sharing
are all re-derived each time. They are properties of the traversal, not a cached
analysis, so they cannot go stale either.

The scaling limit is the graph walk, which is linear in reachable nodes. A patch
of several thousand modules edited by continuous slider drag would eventually be
worth measuring. Nothing approaches that, and if it did, the first fix is
throttling recompiles to the frame timer rather than making compilation
incremental.

`Emitter.Constant` de-duplicates by value, so the register file does not grow
with repeated knob values within a compile.
