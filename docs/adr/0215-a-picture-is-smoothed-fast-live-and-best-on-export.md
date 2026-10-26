# ADR-0215: A picture is smoothed fast live and best on export

**Status:** Accepted · 2026-10-26 · *user-directed* · a pass after
[0035](0035-a-glsl-backend-for-the-video-path.md)'s shader and
[0214](0214-the-picture-draws-through-metal-on-apples-machines.md)'s, and a size for
[0157](0157-flyback-cli-render-draws-on-the-gpu.md)'s render

## Context

Asked for anti-aliased 3D, the Raymarch's edges were the worst of it, but every
picture has the same cause: one sample at each pixel's center. A game's hardware
anti-aliasing tests several points a pixel only against triangles, and a Flyback
picture is one rectangle whose program runs per pixel, so it does nothing here.

Blending each shape's outline into what lies behind it inside the Raymarch was
tried and dropped: a ray that lands cannot tell how near it is to the silhouette,
so only the outer half of an edge softened, and against a sixteen-sample reference
of Flythrough it came out no closer than without it.

## Decision

**Three settings of one thing, `Smoothing`: Off, Fast and Best.** Fast is FXAA,
a pass over the finished frame that blends along the edges it finds: about a
millisecond, on any picture. Best is four samples a pixel, the frame drawn at
twice the width and height and averaged in fours: clean, and four times the
drawing.

**Fast while playing, Best on export.** The preview and a recorded take smooth
Fast, a setting in the Picture section; `flyback-cli render` smooths Best unless
`--smooth` says otherwise, since a render's speed is nobody's frame rate.

**After the history, never in it.** A feedback loop reads the frame as the patch
drew it, so a loop is not smoothed again every frame it goes round.

**One pass on every driver.** The processor's `Fxaa` and the shaders' pass take the
same steps and read bilinear and clamped at the border, so a frame is smoothed
alike wherever it is drawn.

## Consequences

- A render of Flythrough at 960x540 on the processor is about 2.5 times as slow at
  Best, and the error against a sixteen-sample reference falls from 3.1 to 1.2
  levels; Fast takes it to 2.6.
- `--smooth off` is the frame the patch draws, byte for byte, as before.
