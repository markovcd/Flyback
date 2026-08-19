# ADR-0043: A Scan is a Probe read backwards

**Status:** Accepted · 2026-08-19 · *user-directed* · extends
[0040](0040-a-probe-is-a-second-compile-root.md) with the other direction, and
uses [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)'s flag to serve
both sinks from one lowering

## Context

[0040](0040-a-probe-is-a-second-compile-root.md) built a scope out of nothing but
arithmetic: a chart of a signal is a function of `(x, y, t)`, so a Probe pushes a
time that varies across the picture and lowers its input under it. Audio becomes
image, and nothing is measured, kept or read back.

The other direction had no module. It did have a knob: the Output's `scan`
sweeps `x = fract(t·rate)` and drives the whole audio render off the picture
([`AudioScan`](../../src/Flyback.Core/Render/AudioRenderer.cs)). Three things are
wrong with it as the answer to *image becomes audio*.

It is **render-wide**. `AudioScan.For` reads the knobs rather than the sockets,
and says why: *"Sweeping the sweep is the one thing the sockets cannot do."* A
patch is scanned or it is not; there is no sonifying one branch and mixing it
with an oscillator.

It is a **raster**, and a raster's retrace is a discontinuity. `fract` jumps from
1 to 0 once a line, so the waveform has a step in it every cycle whatever the
picture holds — which is a sawtooth, with the full harmonic series, present
before the image contributes anything. This is why sonified images famously all
sound alike: the ear is mostly hearing the scan.

And it lives **outside the program**, in a host-side loop, so it cannot be
patched, modulated, or reasoned about by the compiler.

The asymmetry underneath is real and is not the knob's fault. A Probe
*reparameterises* — a pure function of `t` can be evaluated at any `t`, including
the future, which is why the right half of a chart is honest. Going the other way
is a *projection*: 2-D to 1-D, and a projection needs a path. Choosing the path
is the whole design.

## Decision

**The path is a closed loop, and that is the load-bearing choice.** A circle is
C∞ and periodic, so the sweep contributes no discontinuity at all and one turn is
one cycle of a waveform whose entire shape is the image. What is left is
wavetable synthesis with the field as the table: `radius`, `x` and `y` pick which
loop through it is read, and moving them is the table sweep. That is the
expressive control this module is for, more than the pitch is.

**The sweep is a domain push, so it costs nothing.** `Scan` marks `in` as
[`PortSpec.Swept`](../../src/Flyback.Core/Graph/Ports.cs) exactly as the Probe
does, and pushes an `(x, y)` where the Probe pushes a `t`. The subtree is lowered
once and evaluated once per sample, reading two different registers. Nothing is
added: the compiler emits one program for both backends
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)), so `LoadX` and
`LoadY` cannot be folded away and every audio render *already* walks the whole
spatial graph at the origin. Sweeping is the same op stream with different
numbers in it.

**Where on the loop an evaluation sits is chosen on the memory flag, not by
lowering twice.** The ear is at a moment and the eye is at a pixel, and one
program serves both. So the bearing is a `Mix` on
[`Emitter.HasMemory`](../../src/Flyback.Core/Compile/Emitter.cs): the speakers
take an accumulated phase, the screen takes the pixel's own angle from the
centre. This is [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)'s
flag used for what it was named for — a decided answer on the video path instead
of an emergent one — and it costs three ops rather than a second copy of whatever
is being scanned.

**The phase is accumulated, not multiplied out.** `rate` is a socket, and
[0030](0030-oscillators-accumulate-their-phase.md)'s reason applies unchanged: a
rate that steps bends the waveform rather than breaking it, so a Scan can be
played from a Note.

**The picture is the X-Y display.** Every pixel lands on the point of the loop it
is looking at, so the loop can be drawn where it runs with the value it is
passing over swinging the trace off it. The Probe is the Y-t mode of an
oscilloscope and this is the X-Y mode, from the same mechanism, and the pair is
now complete.

## Consequences

**Only a circle, because only a circle inverts cheaply.** The display asks
*which point of the loop is at this bearing*, and a circle answers in closed form
— one `Atan2`, one `Cos`, one `Sin`. A Lissajous figure has no such inverse, so
supporting one means marching samples per pixel across 31M pixels a second, or
giving up the display. The Probe gets off lightly here because time-across-`x` is
one lookup per column; an X-Y trace has no equivalent shortcut. A general path
would want a coarse polyline distance and a tolerance knob, and is a different
decision.

**A loop along a contour is silent, and nothing about the picture says so.** A
circle concentric with Rings sits on one ring for the whole turn, reads a
constant, and is taken entirely by the DC blocker. This is the module's sharpest
edge: the screen looks identical either side of the mistake. It is named in the
module's own description, the shipped preset is deliberately built off-centre,
and a test pins both halves of it.

**There is no aperture, and the reason is not cost alone.** Scanning fine detail
aliases — a circle of some 3000 px circumference through a two-pixel feature is
1500 cycles a turn, which at 220 Hz is 330 kHz, past Nyquist even at
[0023](0023-oversample-the-audio-path.md)'s 4×. The obvious fix is *K* taps of
the swept subtree per sample, and it is affordable in throughput terms but not on
the thread it would run on: `AudioRenderer` is single-threaded on a callback by
design, video misses a frame where audio misses a buffer, and *K* multiplies the
only budget that has no slack.

Worse, it would be quietly wrong. `Phase` and `Delay` advance their state once
per evaluation, so *K* taps **at one moment and K positions** would run any
oscillator inside the swept subtree *K* times fast and shrink every delay by *K*.
Oversampling does not have this problem, because there the *K* evaluations are at
*K* different times — which is the whole point of it. So the cheap lever is
raising `Oversample` where a Scan is present, reusing a real anti-alias filter
that already exists and is already correct; tap-based aperture needs either a
refusal on a subtree with a non-zero `PhaseCount` or per-tap delay state, and is
not being built on the way past.

**The Output's `scan` knob stays.** It is the render-wide raster and this is a
per-branch loop; they are different instruments, and the knob is what a patch
saved before today still means.

**Both of a Scan's outputs are emitted wherever one is reached.** `Resolve` calls
`Emit` once and returns every result, so a Scan feeding only the speakers still
lowers its display. Some twenty dead ops a sample, against a patch's hundreds —
noted rather than fixed, because nothing anywhere in this engine prunes an unused
output and doing it for one module would be the wrong place to start.

**The first audio sample is the picture's answer.** `HasMemory` reads zero until
something has written it, so evaluation zero takes the screen's bearing. One
sample in 48,000, into a DC blocker that is settling anyway. The filter in
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) already lives with
the same thing.
