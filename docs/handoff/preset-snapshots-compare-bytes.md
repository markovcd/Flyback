# The preset snapshots compare file bytes, not pixels

Diagnosed on 2026-09-25 in a Linux cloud session. The fix is for a Windows session to
make and check. It is on TODO.md; take it off there, and delete this file, in the
commit that lands the fix.

## Symptom

All 25 `PresetSnapshotTests.Preset_renders_as_approved` cases fail in the cloud
container (Ubuntu 24.04), and only there. They pass on Windows and in the Docker
gate. Every other test in the solution passes in the same container.

## What differs: the compressed bytes, nothing else

Every received PNG was decoded and compared with its verified PNG:

| | All 25 presets |
|---|---|
| Decoded pixels | identical |
| Filtered scanlines, before compression | identical |
| Chunk list, zlib header (`78 9c`) | identical |
| File size | differs, e.g. Plasma 16155 against 16462, Empty 247 against 248 |

The renderer is producing the same picture. Only `DeflateStream`'s output differs.

## Why: two builds of the same runtime compress differently

`PngWriter.Compress` uses `new DeflateStream(buffer, CompressionLevel.Optimal)`.
What that produces depends on which zlib the runtime's
`libSystem.IO.Compression.Native` was built against:

- **The container** runs Ubuntu's source-built .NET 10.0.12 (`/usr/lib/dotnet`, RID
  `ubuntu.24.04-x64`, SDK 10.0.112). Its native library links the system's classic
  zlib, `/lib/x86_64-linux-gnu/libz.so.1`.
- **Microsoft's builds** (the Windows install, `mcr.microsoft.com/dotnet/sdk:10.0` in
  the Dockerfile, and the `Microsoft.NETCore.App.Runtime.linux-x64` 10.0.12 package)
  bundle **zlib-ng 2.2.5**.

Same level, two deflate implementations, two byte streams for identical input.

**Proof:** I made a private copy of the container's runtime and replaced only
`libSystem.IO.Compression.Native.so` with Microsoft's copy from the NuGet runtime
package. With `DOTNET_ROOT` pointed at that copy, all 25 cases pass. Nothing else
changed: the same test binaries, the same runtime version.

## The real bug: the pixel comparison never happens

`tests/Flyback.Core.Tests/ModuleInit.cs` says:

> Snapshots are compared as decoded pixels, not as file bytes. [...] PngWriter
> compresses through DeflateStream, whose output may change across runtime versions
> [...] and neither should fail a test.

That is the intent, but it is not what runs. `VerifyImageMagick.Initialize()` registers
no image comparer, so Verify falls back to comparing file bytes. The exact failure
this comment was written to prevent is the one happening in the container. On
Windows it passes only because the approved files were written by the same zlib-ng.
A .NET update that moves zlib-ng, or a Linux distro's own .NET, fails all 25.

## Why `RegisterComparers()` is not the fix

I tried both obvious one-liners in the container:

- **`VerifyImageMagick.RegisterComparers(threshold: 0)`:** fails all 25 even though the
  pixels are identical. The message is `diff(0) > threshold(0)`: ImageMagick's
  measured difference is not exactly nought for identical images.
- **`VerifyImageMagick.RegisterComparers()`** (the default threshold): passes all 25.
  But it is far too lenient for a test whose job is catching rendering regressions:
  - A render with one pixel's blue channel changed by 1 passes in **all 25** presets.
  - A 4×4 block with green raised by 16 passes in **24 of 25**.

## Recommended fix

Compare decoded pixels exactly, with the engine's own reader:

1. In `ModuleInit`, register a Verify stream comparer for the `png` extension
   (`VerifierSettings.RegisterStreamComparer("png", ...)`). It decodes both streams
   with `Flyback.Core.Render.PngReader.Read(Stream, out PngFault)` and passes only when
   the width, the height and every pixel byte are equal.
2. Say where a mismatch is: how many pixels differ, and the first differing
   `(x, y)` with both values. A mismatch then points at the change rather than at a
   file.
3. Drop `VerifyImageMagick.Initialize()` if nothing else needs it. Nothing in
   `Flyback.Core.Tests` uses it besides this line. Removing the `Verify.ImageMagick`
   package afterwards is a package change, so it goes in a commit of its own
   (`.claude/rules/packages.md`).
4. Rewrite the `ModuleInit` comment so it states what now happens. The sentence about
   `MathF` differing across architectures does not hold under an exact comparison.
   The gate runs the tests on x64 only, so it is not a live problem. If arm64 ever
   runs these tests, a tolerance of a level or two per channel is the answer, not
   ImageMagick's threshold.

## How to check the fix

On Windows:

1. `PresetSnapshotTests` passes, as it does now.
2. **It still catches a change.** Temporarily nudge one pixel after the warm-up loop
   in `PresetSnapshotTests`, e.g. `buffer[stride * 90 + 160 * 4] ^= 1;`. Every case
   must fail, and the message must name `(160, 90)`. Take the nudge out again.
3. The full suite passes: `./tests/Flyback.Core.Tests/bin/Release/net10.0/Flyback.Core.Tests.exe`.

A Linux cloud session can then confirm the 25 pass against Ubuntu's .NET as well.
