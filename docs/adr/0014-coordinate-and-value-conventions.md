# ADR-0014: Coordinate and value conventions

**Status:** Accepted · 2026-08-11

## Context

Every module that touches space or color has to agree on what the numbers mean.
Get this wrong and patches stop being portable between resolutions, circles come
out as ellipses, or maths that reads naturally produces something off-screen.

Three conventions had to be fixed: the coordinate range, the aspect handling, and
what an output value of 1.0 means.

## Decision

**y runs −1 to 1, bottom to top.** Screen rows are inverted on the way in:

```csharp
var py = 1f - 2f * (y + 0.5f) / height;
```

**x is the same scale, widened by aspect.** `x` spans −aspect..aspect rather
than −1..1:

```csharp
var px = (2f * (x + 0.5f) / width - 1f) * aspect;
```

**Output is 0..1 per channel, clamped, with no gamma applied.** `Saturate`
clamps and rejects non-finite values; `ToByte` multiplies by 255.

Pixel centres are sampled — hence the `+ 0.5f`.

On the GPU backend ([0035](0035-a-glsl-backend-for-the-video-path.md)) all three
of these fall out of the rasteriser: an interpolated `vUv` lands on
`(index + 0.5) / size` already, so the half-pixel offset is free, and the two
lines above are the only arithmetic the shader's `main` does before the program.
The y-up inversion is the convention most easily broken by a backend, and it is
the one worth checking first when a picture comes out wrong.

## Consequences

Patches are resolution-independent. The same patch at 320×180 and 1920×1080
produces the same image at different sample densities, which is what makes the
preview honest and the 1920×1080 frame export meaningful.

y-up matches the maths rather than the framebuffer. `sin(y)` curves the way it
does on paper, and `Rotate` turns anticlockwise for a positive angle. The single
inversion lives in the renderer's row loop; nothing downstream thinks about it.

Aspect-widening x rather than normalising both axes to −1..1 keeps circles
circular. `Length(x, y)` is a true radius, so `Rings`, `Kaleidoscope` and
`To polar` behave correctly on any window shape. The cost is that x's range
depends on the frame's aspect ratio, so a pattern keyed to x's extremes shifts
when the aspect changes — the trade is worth it, since distorted circles would
affect every radial module.

No gamma correction is deliberate. What a module computes is what the pixel
gets, so a `Value` of 0.5 is byte 128. Applying sRGB encoding would be more
correct for physical light, but it would mean the numbers on a node no longer
predict the output, and for an instrument that directness matters more than
photometric accuracy. It also means gradients are perceptually darker in the
midtones than a color-managed renderer would produce.

Clamping at 0..1 discards headroom. A patch computing 4.0 sees the same white as
one computing 1.0, so there is no HDR range to pull back down with a later `Gain`.
This is also what makes feedback stable
([0012](0012-feedback-as-a-module-not-a-cycle.md)) — the clamp is what stops a
loop with gain above 1 diverging — so the two decisions are linked and would have
to be revisited together.
