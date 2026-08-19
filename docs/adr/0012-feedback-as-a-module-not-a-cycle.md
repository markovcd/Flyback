# ADR-0012: Feedback is an explicit module, not a graph cycle

**Status:** Accepted · 2026-08-11 · amended by
[0035](0035-a-glsl-backend-for-the-video-path.md), where the history becomes a
pair of textures and loses some of the precision this record asks for

## Context

Feedback is the defining effect of analogue video synthesis — a camera pointed
at the monitor showing its own output, producing tunnels and spirals that no
single-pass function can generate. Any video synth without it is missing the
thing that makes the medium distinctive.

The tempting model is to let the graph contain cycles: wire Output back into a
`Rotate` and let the evaluator figure it out. That is what the effect *looks*
like on a physical rig.

It does not survive contact with per-pixel evaluation. A cycle in the graph is
not a cycle in time — the pixel being computed cannot depend on itself. What it
actually depends on is a *different pixel* from the *previous frame*. Cycles in
the graph would have to be silently reinterpreted as frame delays, and the point
where that reinterpretation happens would be invisible in the patch.

## Decision

Feedback is a source module with coordinate inputs and a color output:

```
feedback(x, y) -> color     // samples the previous frame at (x, y)
```

The graph stays a DAG. `SampleFeedback` reads a `FeedbackFrame` handed to
`Evaluate`, bilinearly interpolating the previous frame in patch coordinates and
clamping at the edges. `SynthRenderer` keeps two `float[]` buffers and swaps them
after each frame.

## Consequences

The frame delay is visible in the patch: there is a module named `Feedback` and
it is a *source*, which is exactly what it is. Nothing is silently reinterpreted,
and [0011](0011-compile-backwards-from-output.md)'s cycle detection can treat any
remaining cycle as the error it is — and say so, pointing at this module.

Feedback composes with the whole `Space` category rather than being a special
effect. Sampling at rotated coordinates spins the previous frame; sampling at
scaled coordinates zooms it; both together give the tunnel. The `Feedback tunnel`
preset is `Rotate → Scale → Feedback → Gain`, and every part of that is an
ordinary module.

History is kept in linear float RGB, not as bytes. A feedback loop re-reads its
own output every frame, so 8-bit quantisation would compound — after thirty
frames a slowly dimming image would visibly posterise into bands. Floats cost
12 bytes per pixel (6 MB of history at 960×540) and avoid it entirely.

On the GPU backend ([0035](0035-a-glsl-backend-for-the-video-path.md)) the same
history is a ping-ponged pair of `RGBA16F` textures: eleven bits of mantissa
rather than twenty-four, which is well clear of the eight this is about. Where
half floats are not color-renderable it falls back to eight and the status bar
says so — the posterising described above is then exactly what happens, and
naming it is the least that record can do.

Values are clamped to 0..1 as they are written
([0014](0014-coordinate-and-value-conventions.md)). This is what stops a loop
with gain above 1 from running away to infinity — it saturates to white instead,
which is what an overdriven video feedback loop does in reality.

Bilinear sampling means each feedback pass slightly blurs. That is characteristic
of the real effect, but it does mean feedback cannot be used as a lossless frame
store. Nearest-neighbour sampling would be a second opcode if that were ever
wanted.

`Rewind` clears the history as well as resetting time, so a feedback patch
restarts from black rather than from whatever was on screen.

The one thing this model cannot express is feedback *within* a frame — an
iterative solver, or a reaction-diffusion step evaluated several times per frame.
That would need multiple passes per frame, which the renderer does not have.
