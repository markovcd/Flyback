# ADR-0027: Delay lines give the audio path a memory

**Status:** Accepted · 2026-08-12 · *user-directed* · amends
[0006](0006-scalar-interpreter-parallel-over-rows.md) and
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)

## Context

A delay and a reverb are memory of past samples. The machine had none: every op
read and wrote registers, so `Evaluate` was a pure function of `(x, y, t)` and
the one thing resembling history — `SampleFeedback` — reads a *frame*, which
`AudioRenderer` does not have and passes `default` for.

That made these two effects not merely unwritten but inexpressible. A plugin
contributes a `NodeDef` and an emit function over **existing** opcodes
([0026](0026-modules-from-plugins-with-provenance-in-the-file.md)); the
interpreter is a switch in `Flyback.Core`. No plugin can introduce state, however
much it is asked to.

Two alternatives were weighed and rejected. Re-emitting the upstream subgraph at
`t − d` keeps everything pure — the whole patch really is a function of `t` — but
feedback has to be unrolled one repeat per copy, and a reverb tail is thousands
of taps. Building both on the existing frame feedback gives a delay and a reverb
for the *picture*, which is what
[0012](0012-feedback-as-a-module-not-a-cycle.md) already provides and not what
was asked for.

## Decision

Two stateful opcodes, `Delay` and `Allpass` — a feedback comb and a Schroeder
allpass, which between them build both effects.

**A line's identity is its position among the stateful ops**, counted as the
program runs. Every op executes exactly once per evaluation and always in the
same order, so this is exact and costs no field on `Op`. `K` carries the longest
delay the instance may be asked for, which is what sizes the buffer; the delay
itself stays a signal and may be swept, and reads interpolate so that sweeping
glides rather than steps.

**The state belongs to the renderer, not the program.** `DelayState` hangs off
`AudioRenderer` for the same reason [0018](0018-never-render-frames-on-the-ui-thread.md)
keeps a `CompiledPatch` immutable: a recompile swaps the program under the audio
thread and two programs may briefly both exist. State on the program would be
duplicated or lost at exactly that moment.

**A line is read before it is written**, so the shortest delay it can express is
one evaluation. Writing first would make a zero-length feedback loop algebraic,
and there would be nothing for it to mean.

**Feedback is clamped below one and the write is bounded.** [0013](0013-guard-arithmetic-instead-of-propagating-nan.md)
guards degenerate arithmetic; this is the same rule applied to something that
persists. Every other op in the machine forgets a bad value immediately. A delay
line at a feedback of one never decays, and above it doubles every pass — turning
the knob back down would not undo it.

## Consequences

**`Evaluate` is no longer a pure function of `(x, y, t)`.** That purity is what
[0006](0006-scalar-interpreter-parallel-over-rows.md) relies on to render rows in
parallel, so the video path passes no state at all — and there a delay hands its
input straight through, because a shared line has no per-pixel meaning and a
patch built for the speakers still has to draw something.

**This is the first real asymmetry against [0022](0022-audio-and-video-are-two-sinks-over-one-patch.md).**
Until now a module did the same thing whichever sink compiled it. These two do
their job in one program and are wires in the other. The alternative was to
refuse the modules, and the asymmetry is visible in the palette — they are in a
category of their own and say so in their descriptions.

**The Reverb cannot even be a wire.** A comb's gain at DC is one over one minus
its feedback, so its wet path is scaled by exactly that to come out at unity;
with no state the combs are wires and the scaling is the only thing left, so the
picture dims by it. Any normalisation that depends on the feedback breaks the
identity and one that does not would make the reverb's level depend on its decay.
Correct levels where the module is actually used won, and the tests pin the
dimming rather than claiming it does not happen.

**Lines run at the oversampled rate** ([0023](0023-oversample-the-audio-path.md)),
because that is how often the program is evaluated. The Delay module's two-second
maximum is therefore 384,000 samples — about 1.5 MB — which is why the maximum is
fixed at compile time and only the delay within it is a signal.

**A recompile that changes the delays gets fresh buffers**, and whatever was
ringing is lost. The tail belonged to a patch that no longer exists. Sizing
happens in `Prepare`, which `AudioEngine` already calls on the UI thread, with
`Render` as the backstop it always was.

What this does not do: there is no damping inside the comb loop, so the tail is
brighter than a real room's; the reverb is mono, where the usual trick is a
second bank offset by a few samples; and a patch cannot delay a colour, because
three buffers per line would buy nothing that
[0012](0012-feedback-as-a-module-not-a-cycle.md) does not already do better.
