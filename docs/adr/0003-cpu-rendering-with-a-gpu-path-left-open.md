# ADR-0003: Render on the CPU, leave a GPU backend possible

**Status:** Accepted (user-directed) · 2026-08-11

## Context

Generating images from maths per pixel is exactly what fragment shaders exist
for. The obvious high-performance answer is to compile the patch to GLSL/HLSL
and run it on the GPU. The obvious simple answer is to evaluate it on the CPU.

Three options were put to the project owner:

1. **CPU now, GPU later** — evaluate per pixel on the CPU. Simple, debuggable,
   pure C#, works everywhere. Architecture leaves room for a shader backend.
2. **GPU shaders from the start** — true 1080p60, but the core becomes a
   compiler emitting shader source, and debugging means reading generated GLSL.
3. **Offline only** — library plus CLI, PNG sequences, no realtime, no window.

The owner chose option 1.

## Decision

Render on the CPU. Structure the pipeline so a GPU backend can be added later
against the same intermediate representation rather than requiring a rewrite.

## Consequences

The concrete thing that keeps the GPU door open is
[0005](0005-compile-to-a-flat-register-machine.md): patches lower to a flat list
of ops, each of which is one line of arithmetic on named registers. A GLSL
backend consumes that same `Op[]` and *emits* `float r7 = r4 * r6;` instead of
interpreting it. The graph walk, topological ordering, dead-code elimination,
type coercion and constant folding are all already done by the time a backend
sees the program, so a second backend inherits them.

Two things would not transfer. `SampleFeedback` reads a CPU-side float buffer
and would become a texture sample against a ping-pong framebuffer. And the
numeric guards in [0013](0013-guard-arithmetic-instead-of-propagating-nan.md)
are written as C# branches; on a GPU they would need to be branchless or
accepted as divergence.

The measured cost of staying on the CPU is lower than expected: 3.5 ms per
960×540 frame in the running app, 5.3 ms in a headless Release benchmark. That
is 3–6× headroom at 60 Hz, and 1080p is comfortably reachable. The GPU backend
is therefore not urgent, which is the outcome that makes this decision cheap.
