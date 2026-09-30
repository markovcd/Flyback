# ADR-0167: The shader reads a table as a float texture

**Status:** Accepted · 2026-09-30 · *user-directed* · implemented in
`src/Flyback.Engine/Compile/GlslEmitter.cs`, `src/Flyback.Gpu/TableTextures.cs` and
`src/Flyback.Web/SoundState.cs`; replaces the processor fallback of
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) and
[0053](0053-a-scope-records-what-the-speakers-played.md)

## Context

A table read, a Sample's clip or a Scope's or an Analyzer's chart, had no GLSL: the
shader read it as silence, and a program carrying one was drawn on the processor. On
the desktop that made a Scope patch slow; in a page, where the processor is too slow
to draw at all and the picture is WebGL alone, a Scope drew a flat line.

## Decision

**Each table is an R32F texture, in rows of 4,096 floats, read with `texelFetch`.**
`GlslEmitter` declares a sampler, a length and a rate per table, and `tab` reads two
texels and interpolates between them as `LoadedSample.At` does, with silence off either
end. Filtering is by hand because a float texture filters only by extension. A clip is
uploaded once and kept while the program reads it; a chart's buffer is uploaded again
every frame it is drawn, since the sound refills it.

**No program is handed to the processor for what it reads.** The preview stays on the
GPU it was asked for; the processor draws only when chosen, or when the GPU fails on
the desktop. In a page it never draws.

**The web viewer's worker hands the page each chart's buffer**, after the Meters'
readings, and the web editor names its charts to the worker as it names its Meters.

## Consequences

- The GPU and the processor agree on a Scope and on a Sample to ADR-0035's bounds
  (`GpuRenderTests`). A clip's position is a 32-bit float in the shader, so a read
  more than a few minutes into a long clip lands a fraction of a sample away from the
  processor's.
- A clip longer than about three minutes at 48 kHz needs more rows than the 2,048
  every WebGL 2 guarantees, and reads as silence on a card that refuses them.
- A Scope costs an 8 KB upload a frame, and in a page 8 KB more a message from the
  worker.
