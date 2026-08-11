# ADR-0019: No third-party dependencies in the engine

**Status:** Accepted · 2026-08-11

## Context

`VideoSynth.Core` needs two things that are conventionally solved by taking a
package: writing PNG files, and generating coherent noise.

For PNG, the usual answers are ImageSharp (licensing changed to a split
commercial model), SkiaSharp (a large native dependency, one per platform), or
`System.Drawing.Common` (Windows-only since .NET 6).

For noise, the usual answer is a library or a copied Perlin implementation with a
512-entry permutation table.

Both would work. Both would also mean Core acquires a dependency for something
peripheral to what it is.

## Decision

Neither. `PngWriter` and `Noise` are written by hand against the BCL.

**PNG** is 109 lines. PNG is a signature, three chunks, and a zlib stream:
`DeflateStream` provides the compression, and the zlib header, Adler-32 trailer
and per-chunk CRC-32 are a few lines each. Filter type 0 on every row — no filter
heuristics, no interlacing, no palette.

**Noise** is 41 lines of hash-based value noise. A cheap integer hash of the
lattice coordinates replaces a permutation table, so it is stateless and needs no
seeding or initialisation.

## Consequences

Core references nothing outside the BCL, which is what makes
[0002](0002-split-engine-from-shell.md) worth having: frame export works
headlessly, on any platform, with no native assets to deploy.

Writing the PNG encoder before the UI existed is what made the engine verifiable
early. A console harness rendered every preset to a real image file — which is
how the maths was confirmed, by looking at the output rather than trusting it.

Stateless noise means `Noise3` is a pure function of its three inputs. There is
no initialisation order, no seed to thread through the compiler, and no shared
table for parallel rows to contend on. It is also trivially portable to a GPU
backend ([0003](0003-cpu-rendering-with-a-gpu-path-left-open.md)), where a
permutation table would have needed a texture upload.

The costs are bounded but real. The encoder produces larger files than a tuned
one — filter type 0 everywhere means no delta encoding, so a smooth gradient
compresses worse than it should. It writes 24-bit RGB only, so alpha is dropped
and there is no 16-bit output. And it is code that now needs maintaining, though
PNG is a frozen format and this covers the part of it that matters.

Value noise is smoother and less detailed than Perlin or simplex — fewer
directional artefacts, but also less character. For a module driven through
`Warp` and `Kaleidoscope` that is acceptable; a `Simplex` module would be a
second opcode if the difference ever mattered.

This does not extend to the shell. `VideoSynth.App` takes Avalonia
([0015](0015-avalonia-for-the-ui-shell.md)) without hesitation, because writing a
windowing and layout toolkit is not the same kind of trade at all.
