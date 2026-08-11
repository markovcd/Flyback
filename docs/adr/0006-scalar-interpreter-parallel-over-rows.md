# ADR-0006: Scalar interpreter parallelised over rows, not SIMD

**Status:** Accepted · 2026-08-11

## Context

Given a flat op list ([0005](0005-compile-to-a-flat-register-machine.md)), there
are two axes of parallelism available on a CPU, and they compose.

**Across cores.** Pixels are independent — nothing in the model lets one pixel
observe another within a frame — so rows can be rendered concurrently.

**Within a core.** The register file could be `Vector<float>[]` instead of
`float[]`, processing 8 or 16 pixels per op. This is how a shader-like workload
is normally made fast on a CPU, and the op list is already in the right shape
for it.

The initial estimate was that a scalar interpreter would cost roughly 90 ms per
960×540 frame — far too slow — which would have made SIMD mandatory.

## Decision

Scalar interpreter, parallelised over rows with `Parallel.For`, using the
`localInit` overload so each partition allocates its register scratch once:

```csharp
Parallel.For(0, height, patch.AllocateRegisters, (y, _, registers) => { ... }, _ => { });
```

No SIMD. Measure first.

## Consequences

The estimate was wrong by more than an order of magnitude. Measured throughput
is 5.3 ms per 960×540 frame in a headless Release benchmark and 3.5 ms in the
running app — 188 fps of raw capacity where 60 was the requirement. Parallelism
across cores alone was sufficient, and the work SIMD would have bought was never
needed.

Avoiding SIMD kept the interpreter readable: `CompiledPatch.Evaluate` is a plain
`switch` where every case is one line of `MathF`. The guard behaviour in
[0013](0013-guard-arithmetic-instead-of-propagating-nan.md) is expressed as
ordinary branches (`b == 0f ? 0f : a / b`), which under vectorisation would each
have become a select over a mask.

If SIMD is ever needed, the change is contained. The op list does not change,
the compiler does not change, and the node catalogue does not change — only
`Evaluate` and the register type. The branchy guards are the part that would
need rethinking.

`Parallel.For` on the UI thread turned out to be actively dangerous, for reasons
unrelated to performance — see [0018](0018-never-render-frames-on-the-ui-thread.md).
