# ADR-0225: Smoothing is one setting, and it can be Best

**Status:** Accepted · 2026-11-05 · *user-directed* · supersedes
[0215](0215-a-picture-is-smoothed-fast-live-and-best-on-export.md)'s "Fast while playing, Best on export"

## Context

ADR-0215 kept Best, four samples a pixel, for `flyback-cli render`, on the grounds that a
render's speed is nobody's frame rate. Asked to pick Best in the editor too, the
question became whose frame rate it is: on a graphics card most patches have the
headroom, and the person playing is the one who can judge the trade. A render that
defaulted to Best whatever the editor said also broke the rule the other settings keep,
[0168](0168-the-sound-is-oversampled-2x-and-a-setting.md)'s oversampling among them: the
editor, its takes, `flyback-cli render` and `flyback-viewer` all follow one setting.

## Decision

**Settings → Picture → Smoothing offers Off, Fast and Best, and it is the default
everywhere**: the preview, a recorded take, `flyback-cli render` and `flyback-viewer`.
`--smooth` on either command overrides it, and `flyback-viewer --smooth` takes `best`.

**Best draws the history at twice the width and height, and halves it on the way
out.** On OpenGL, OpenGL ES and WebGL the halving is a read of the history at each
output pixel's center, which falls on the corner four texels share, so the card's
bilinear filter averages them in one tap; Metal's resolve does the same. The
processor draws through `SmoothedFrames`, as a render does.

**A feedback loop runs at the doubled size**, as it does in a Best render, so the
preview and an export of it agree. Switching to or from Best starts the history from
black, as a new size does.

**A size the card will not double is drawn Fast**, rather than handing the picture
to the processor.

## Consequences

- Best is four times the drawing. On a graphics card a light patch keeps its frame
  rate; a heavy one, or any patch on the processor, plays at about a quarter of it.
- Off and Fast are unchanged. A render left without `--smooth` is Fast unless the
  editor was set to something else, where it used to be Best.
- `flyback-cli render` still wraps the renderer in `SmoothedFrames` for Best, where
  it could hand the GPU renderer Best itself.
