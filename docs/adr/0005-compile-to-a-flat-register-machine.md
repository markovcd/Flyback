# ADR-0005: Compile patches to a flat register machine

**Status:** Accepted · 2026-08-11

## Context

A patch is a graph of modules, and it has to be evaluated once per pixel — about
518,000 times per frame at 960×540, sixty times a second. Whatever happens
between "graph" and "pixel" is on the hottest path in the program.

Four ways to get there:

1. **Walk the node graph per pixel.** Each node exposes `Evaluate(x, y, t)` and
   recurses into its inputs. Simplest to write. Every input is a virtual call,
   shared subexpressions are recomputed once per consumer, and the working set
   is a pointer-chase through scattered objects.
2. **Build a tree of delegates.** Compile each node to a `Func<...>` closure once,
   then invoke the root per pixel. Removes the graph walk but keeps one indirect
   call per node per pixel — roughly 18 million delegate invocations per frame
   for a 20-module patch at 960×540.
3. **Emit IL via `System.Linq.Expressions`.** Fastest per-pixel path; produces
   genuinely compiled code. But the IR is an expression tree that only the CLR
   can consume, debugging means reading IL, and it cannot be retargeted.
4. **Lower to a flat instruction list over a register file**, interpreted by a
   switch.

## Decision

Option 4. `PatchCompiler` topologically flattens the graph into an `Op[]`:

```csharp
readonly struct Op(OpCode code, int outReg, int a, int b, int c, float k)
```

Each op reads up to three `float` registers and writes one (or three, for
`HsvToRgb` and `SampleFeedback`). `CompiledPatch.Evaluate` walks the array once
per pixel through a `switch` on `op.Code`, against a scratch `float[]` the
caller owns.

## Consequences

The inner loop has no allocation, no virtual dispatch and no graph traversal.
Shared subexpressions are evaluated once because they are one op writing one
register that many later ops read — the `Coordinates` module's `x` output feeds
several consumers at the cost of a single `LoadX`. Register scratch is allocated
once per parallel partition, not per pixel.

The instruction list is the thing that makes a GPU backend tractable
([0003](0003-cpu-rendering-with-a-gpu-path-left-open.md)). Each op is one line
of arithmetic on named registers, which is also what shader source looks like.
A backend that emits text instead of executing a switch reuses the entire
front end.

It is also inspectable in a way the alternatives are not. `Op.ToString()`
renders `r7 = Mul(r4, r6)`, and the status bar reports the op count — the Plasma
preset is 33 ops over 35 registers. When a patch misbehaves, the compiled
program can be read.

The cost is one indirection the CLR cannot remove: a `switch` dispatch per op,
where option 3 would have emitted straight-line machine code. The measured
result — 3.5 ms per 960×540 frame — says that cost is affordable, and it buys
retargetability that option 3 cannot offer at any price.

Registers are never reused across values ([0007](0007-register-slots-with-scalar-broadcast.md)),
so the file grows with patch size rather than with live range. At tens of
registers per patch this is irrelevant; a register allocator would be the fix if
it ever were not.
