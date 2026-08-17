# ADR-0035: A GLSL backend for the video path

**Status:** Accepted · 2026-08-17 · discharges
[0003](0003-cpu-rendering-with-a-gpu-path-left-open.md), amends
[0006](0006-scalar-interpreter-parallel-over-rows.md),
[0012](0012-feedback-as-a-module-not-a-cycle.md),
[0013](0013-guard-arithmetic-instead-of-propagating-nan.md),
[0018](0018-never-render-frames-on-the-ui-thread.md) and
[0032](0032-the-registers-are-double-precision.md)

## Context

The preview gets choppy as a patch grows, and it does so for a reason the
architecture already knew about. `SynthRenderer` walks `CompiledPatch.Evaluate`
once per pixel — 518 400 times at 960×540 — and
[0006](0006-scalar-interpreter-parallel-over-rows.md) measured that loop as bound
by its own dispatch rather than by the arithmetic in it. So the cost is linear in
`Ops.Length`, and the instrument gets slower exactly as a patch gets interesting.

Two things in the code make that worse on purpose, and both are correct. The
render loop reserves a core so the audio callback has somewhere to be scheduled,
and `PreviewSurface` idles for half of each frame's cost afterwards for the same
reason. A patch heavy enough to be worth building is a patch whose picture and
sound fight each other for the machine.

[0003](0003-cpu-rendering-with-a-gpu-path-left-open.md) wrote down what to do
about this five days ago and left the door open. Patches lower to a flat `Op[]`
in SSA form, which is the shape a fragment shader wants. This is that door being
used.

## Decision

**The patch compiles to a fragment shader, and the shader draws the preview.**

`Flyback.Core/Compile/GlslEmitter.cs` turns an `Op[]` into GLSL text and does
nothing else — no GL, no platform, no state. It lives in Core so that what the
GPU is asked to run is covered by Core's tests;
[0019](0019-no-third-party-dependencies-in-the-engine.md) is untouched, because a
string builder is not a dependency. Every GL call lives in
`Flyback.App/Controls/GpuFrameRenderer.cs`, against the `Avalonia.OpenGL` that
the Avalonia package already carries. No new package.

**Registers become `float rN` declarations, not an indexed array.**
[0007](0007-register-slots-with-scalar-broadcast.md) already guarantees each
register is written once and always before it is read, so the program is in SSA
form and this is simply legal. An array would additionally force
dynamically-indexable storage, which some drivers put in scratch memory.

**`CompiledPatch.Evaluate` is the specification, and where GLSL disagrees with it
the builtin is not used.** `fract` can round up to exactly 1 where
[0013](0013-guard-arithmetic-instead-of-propagating-nan.md)'s is half-open;
`mod`, `pow` and `atan` are undefined where the interpreter answers; `smoothstep`
divides by zero when its edges meet; `mix` is free to contract into a fused
multiply-add and then disagrees at the endpoints. Each of those is emitted as a
helper transcribing the interpreter instead. `step` is the one op that maps
straight across. The three stateful ops lower to the branch the interpreter takes
when it is handed no state, which is the branch the video path always takes.

**`Const` lowers to a `uK[]` uniform, never to a literal.** This is the decision
the whole feature rests on. [0021](0021-recompile-the-whole-patch-on-every-edit.md)
rebuilds the program on every knob movement, so a baked constant would mean a
`glCompileShader` and `glLinkProgram` per frame of a drag — on Windows, ANGLE
translating to HLSL and invoking the D3D compiler, on the render thread, while
someone is dragging. With the values as uniforms the shader text is byte-identical
across a drag, and a hash of that text is what decides whether to rebuild at all.

**The CPU renderer stays, as three things at once:** the fallback, the frame
export, and the reference semantics. It is no longer *the* renderer, and that
change of status is the point of recording this.

## Consequences

**The frame cost stops tracking the op count.** Measured in the running app at
960×540, switching backends live on the same patch:

| preset | ops | CPU | GPU |
|---|---|---|---|
| Plasma | 31 | 10.6 ms | 0.2 ms |
| Kaleidoscope | 38 | 14.3 ms | 0.8 ms |
| Feedback tunnel | 43 | 15.5 ms | 1.2 ms |

The left column climbs and the right one barely moves, which is the whole claim.

**The two backends agree.** Rendering the same frozen frame both ways and
comparing the preview differed by 0.16 of a byte per channel on average and 9 at
the worst pixel — and that is measured through a screen capture of a steep colour
gradient, so most of it is sampling rather than arithmetic.

**Where the fence goes turned out to matter more than whether there is one.**
Timing around the whole frame reported 16.3 ms for the feedback preset — one
refresh interval, and almost exactly the CPU's own figure, which would have made
the GPU look pointless. A patch that samples its last frame makes this frame wait
on the previous one having been presented, so a fence after the blit measures the
compositor. Fencing the offscreen pass alone reads 1.2 ms, and means what
`SynthRenderer`'s number means: what it took to work out the picture.

**The letterboxed blit cannot use the oversized-triangle trick.** That trick
relies on the viewport clipping the overshoot, and this blit deliberately draws
smaller than its viewport — so the overshoot was *visible*, as a smear of the
picture's top row across the letterbox and a wedge of untouched black where the
hypotenuse cut the corner. It draws a four-vertex strip, which has no overshoot
to leak. There is no y-flip: the offscreen texture and the target are both
framebuffers with the same convention, and they agree.

**The frame history is a texture pair rather than two `float[]`.**
[0012](0012-feedback-as-a-module-not-a-cycle.md)'s mapping is preserved to the
texel — the two scale uniforms carry the whole of `CompiledPatch.Sample`, linear
filtering does its bilinear read and clamping to the edge does its clamp — but
the precision drops from 24 bits to 11, and to 8 on a stack where half floats are
not colour-renderable. The status bar says so when that happens, because a
posterised feedback loop is exactly what 0012 predicts and someone should be told
which one they are looking at. `Rewind` clears both, and so does a resolution
change and a lost context.

**The registers are `double` on the CPU and `float` on the GPU.** There is no
usable `double` in GLSL on any of the three targets. The audio path never touches
any of this — `AudioRenderer` still evaluates in `double` against a `double`
`DelayState` — so the ringing [0032](0032-the-registers-are-double-precision.md)
fixed cannot return. What does arrive is the same mechanism on the picture, where
`Phase` falls back to `a * b + c` and is usually followed by `fract`:

| `t` | `t × 16` | float spacing | steps per cycle |
|---|---|---|---|
| 60 s | 960 | 6.1e-5 | ~16 000 |
| 600 s | 9 600 | 9.8e-4 | ~1 000 |
| 3 600 s | 57 600 | 3.9e-3 | ~256 |
| 36 000 s | 576 000 | 6.3e-2 | ~16 |

A fast oscillator visibly stairsteps after about an hour of continuous playback.
This is recorded rather than fixed because nobody finds an hour-long defect by
testing, and because there is an answer to it: the toolbar carries a GPU switch,
and turning it off is the escape hatch. The real fix — a two-float time uniform
with a Dekker product, emitted only for the `Phase` ops whose input traces to
`LoadT` — is deferred, not forgotten. `exp` also overflows at about 88 again
rather than 710, which is 0032's example moving back to where it started.

**[0013](0013-guard-arithmetic-instead-of-propagating-nan.md) predicted a
divergence; this bounds it.** `Div`, `Mod`, `Sqrt`, `Log`, `Fract`, `Clamp`,
`Step` and `Smoothstep` match exactly, because each is transcribed rather than
mapped. The one place drivers may differ is NaN: `fin()` tests `v == v`, which a
driver built with fast-math is entitled to fold to true, and a NaN reaching the
output would then show white where the CPU shows black. `Sign` is guarded on the
GPU where the CPU is not — `Math.Sign` throws on a NaN, and a shader has nowhere
to throw to.

**[0018](0018-never-render-frames-on-the-ui-thread.md)'s rule holds, by a
different mechanism.** `OnOpenGlRender` runs on the compositor's render thread,
and the deadlock chain that record documents cannot form, because there is no
`Parallel.For` for a re-entrant paint to wait behind. The cost is a hand-off: the
timer moves the clock on the UI thread and publishes a snapshot under a lock, and
the render thread reads it. `Bounds` is read on the UI thread because it belongs
to it.

**[0006](0006-scalar-interpreter-parallel-over-rows.md)'s reserved core and the
rest between frames both stay.** They are properties of CPU rendering, not of the
preview, and CPU rendering does not go away — it runs on exactly the machines
with no GPU to spare. Neither is carried into the GPU surface, which has nothing
to rest from: a frame there is a few dozen uniform uploads and two four-vertex
draws.

**Export stays on the CPU, and the exported PNG may differ in its last bits from
what is on screen.** `SaveFrameAsync` runs on a thread with no graphics context,
its output is what the approved snapshots are, and only the interpreter
reproduces 0013 reproducibly. That divergence is the price of a second backend
and it is smaller than the price of a non-deterministic export. This is the first
record where two backends disagree by design.

**The GPU is not offered again once it has refused.** No context, a shader that
will not build, no renderable offscreen format, or a context lost more than twice
— any of them says so once and hands the frame to the CPU for the rest of the
session. Whatever it was will not have fixed itself by the next frame, and a
preview that flickers between backends is worse than either. A person may still
switch by hand while the GPU is on offer, which is how the two get compared.

**What the tests can and cannot reach.** An opcode-coverage test fails the day an
opcode is added and the shader is not told, and the emitter throws rather than
emitting nothing for one it does not know. The lowering table is approved as
golden GLSL per preset per dialect, which is cheap to review by eye in a way a
rendered frame is not. What none of that reaches is the GPU itself: CI is
headless, and a test that skips itself everywhere is worse than a documented
harness. The agreement figures above were measured by hand.
