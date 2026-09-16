# ADR-0074: A cell is a plane on the video path

**Status:** Accepted · 2026-09-16 · *user-directed* · implemented in
`Compile/PlaneState.cs`, `Compile/CompiledPatch.cs`, `Render/SynthRenderer.cs`,
`Compile/GlslEmitter.cs` and `Controls/GpuFrameRenderer.cs` · extends
[0012](0012-feedback-as-a-module-not-a-cycle.md) from a color to a value and
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) from one sink to
two · bounded by
[0006](0006-scalar-interpreter-parallel-over-rows.md),
[0035](0035-a-glsl-backend-for-the-video-path.md) and
[0064](0064-a-pixel-runs-only-what-a-pixel-changes.md)

## Context

The picture has one kind of memory and it is a color.
[0012](0012-feedback-as-a-module-not-a-cycle.md) keeps the previous frame whole
and lets a patch sample it at any coordinate, which is the effect the medium is
named for. Everything else the machine can remember belongs to the speakers: a
delay line, an accumulated phase, a one-evaluation cell. On the video path
`Evaluate` is handed no state at all, and
[0064](0064-a-pixel-runs-only-what-a-pixel-changes.md) leans on exactly that — a
delay hands its input through, an accumulator is the multiply it replaces, a cell
reads zero — to reorder the program into three stages.

Two things want a memory that is neither of those.

**A loop drawn by hand is silent to the eye.** The Unit Delay
([0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)) is how a patch
holds a cycle, and it is marked `ModuleSinks.Audio` because there is nothing for
it to mean on a picture: `GlslEmitter` lowers `UnitRead` to `0.0` and the
interpreter reads the same nothing. So a patch with a feedback loop in it sounds
like the instrument it is and draws black through the loop, which is the one
place where the premise of
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) does not hold.

**A picture that wants to accumulate has nowhere to put the number.** A
reaction-diffusion step, a trail that decays, a per-pixel envelope follower — each
of them is a value carried by *this* pixel from one frame to the next. Feedback
cannot hold it: it keeps three channels, they are the ones the Output wrote, and
reading them back means routing the value through the picture and out again.

"The previous evaluation" has three readings on a path whose evaluations are
pixels, and only one of them is affordable.

**The previous pixel in scan order** is the literal translation, and it is the one
with the best aesthetics: analog video is a serial scan, so a filter on that
signal smears horizontally and a delay of a line's worth of samples shifts
vertically. It costs the two things that make a frame cheap — rows in parallel
([0006](0006-scalar-interpreter-parallel-over-rows.md)) and the staging whose
soundness rests on the video path passing no state — and it has no lowering at
all, because a fragment shader has no scan order. `GlslEmitter` throws on an
opcode without one so that the two backends never draw different patches, and
this would be the first thing to make that promise unkeepable.

**The previous frame somewhere else** is `SampleFeedback`, which exists.

**The previous frame at this pixel** is what is left, and it turns out to be the
cheap one, because it is local.

## Decision

**A cell may ask for a plane: one value per pixel, read as the previous frame
left it.** The plane belongs to the renderer rather than to the program, for the
reason [0027](0027-delay-lines-give-the-audio-path-a-memory.md) gives — a
recompile swaps the program under a running sink and two programs may briefly
both exist.

**A plane is asked for and not given.** `Emitter.AllocateUnitSlot` keeps its
meaning exactly: a scalar cell, zero where there is no state, which is what
`HasMemory` answers for and what makes a Filter a lowpass at DC on the screen.
Only a cycle breaker claims the new kind. Were every cell a plane, every Filter in
every saved patch would quietly become a *temporal* filter — a one-frame lag
smearing across the picture — and a patch would allocate for a feature it never
asked for. Both are the same mistake, which is deciding on a module's behalf
([0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) says a stateful
module chooses what it does with no state; this says it also chooses to have
some).

**It is written in place, and there is no second buffer.** A pixel reads and
writes only its own slot, and no other pixel can see it. That is the whole
difference from feedback, which keeps last frame entire because it samples at
coordinates a pixel does not own. Rows therefore stay disjoint and
`Parallel.For` over them is untouched.

**A plane's ops belong to the pixel stage.** A value that differs per pixel
cannot be hoisted, and the stable sort of
[0064](0064-a-pixel-runs-only-what-a-pixel-changes.md) keeps the read ahead of
the write as the program was emitted, which is where a cycle's one frame of
latency comes from — the same ordering the audio path's drain relies on. What a
loop costs, then, is that nothing downstream of it can be lifted out of the
pixel stage.

**Float, indexed by pixel, cleared when the size changes.** Float is what
[0012](0012-feedback-as-a-module-not-a-cycle.md) chose for the same reason, and a
plane is half again cheaper than a register ([0032](0032-the-registers-are-double-precision.md)).
Indexed by pixel rather than by coordinate, there is nothing to resample on a
resize and no bilinear blur on the way round: a clear is the honest answer, as it
is for `Rewind`, which empties these alongside the history.

**A plane belongs to the node that claimed it.** `StateOwners`
([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)) already
says whose each cell is, and adoption by owner is what keeps a simulation that
has been running for a minute from being reset by an edit to an unrelated wire.

**On the GPU a plane is one channel of a ping-ponged target.** A shader cannot
read the texture it writes, so there the second buffer comes back: four planes to
a target, and the draw buffers left over from the picture hold something like two
dozen. Precision is [0035](0035-a-glsl-backend-for-the-video-path.md)'s, and its
eight-bit fallback is where this stops being tenable — a color quantized to eight
bits posterises, while an accumulator quantized to eight bits is noise within a
few frames.

**The shader reads its planes by texel and writes them at the end of the pass.**
`texelFetch` at `gl_FragCoord`, because a filtered read would blend in the
neighbours a plane is defined not to see, and a variable per plane held across
the body so that a plane nothing writes this pass carries on holding what it
held — which is what a cell the interpreter never writes does. Full floats first
and half floats second, and eight-bit not offered at all: a color tolerates the
eight-bit ladder [0012](0012-feedback-as-a-module-not-a-cycle.md) describes,
where an accumulator quantized on every pass drifts rather than bands.

**Where a context cannot do that, it says so and the frame is drawn on the
processor.** Three things can be missing — the call that turns the extra
attachments on, a float surface to render to, and, on desktop GLSL 1.50, the
call that says which attachment an output goes to, since that dialect has no
`layout(location = …)` on a fragment output. Each is refused rather than worked
around. Lowering a plane read to zero instead, as `UnitRead` is lowered, would
leave the loop open and put a different picture on the GPU from the one the
interpreter draws, and the two backends agreeing is what
[0035](0035-a-glsl-backend-for-the-video-path.md) rests on.

**The Unit Delay drops `ModuleSinks.Audio`.** One evaluation is a sample to the
ear and a frame to the eye, which is the relation the two sinks already have and
the one `t` already describes. A module that needs the number rather than the
ordinal measures it, as `Emitter.Interval` does
([0042](0042-the-clock-and-the-memory-flag-belong-to-the-emitter.md)).

## Consequences

**A patch with no loop pays nothing.** No plane is claimed, so none is allocated
and no op is added; a patch that never draws a cycle compiles to the program it
compiles to today, register for register.

**A patch with a loop pays one channel of a feedback frame per loop.** A plane is
one float per pixel where the history is three:

| Render size | One plane | Frame history today |
|---|---|---|
| 960×540 | 2.1 MB | 12.4 MB |
| 1280×720 | 3.7 MB | 22 MB |
| 1920×1080 | 8.3 MB | 50 MB |
| 3840×2160 | 33 MB | 199 MB |

Three loops at 1080p is 25 MB against the 50 MB the picture already spends on
being able to see itself. On the GPU the pair costs twice that and the budget is
the draw buffers rather than the memory.

**A loop means the same thing to both sinks, and the patch says where.** Nothing
is reinterpreted silently — [0012](0012-feedback-as-a-module-not-a-cycle.md)'s
objection to cycles in the graph was that the delay would be invisible, and a
module is where it is visible. This makes an implicit cycle *possible* to argue
for later. It does not argue for one.

**A picture built on planes is resolution-dependent in a way feedback is not.**
Feedback resamples, so a patch drawn at preview size and exported at 1080p is
recognisably the same picture. A plane is per pixel, so a reaction-diffusion
patch is a different simulation at a different size, and changing the size clears
it. Preview and export therefore do not share a state, and neither does a render
restarted at a new resolution.

**A frame is not a duration.** A plane is one frame ago, and a frame moves with
the load and with the export settings, so a patch tuned to look right at sixty
will decay differently at thirty. [0048](0048-time-is-seconds-and-nothing-else.md)
is why that is stated rather than hidden: what a module should do about it is
measure the interval, which is already there, and what a patch should do about it
is know.

**A patch that gained or lost a loop rebuilds the framebuffers.** The targets are
attachments, attachments belong to a framebuffer, and how many there are is a
fact about the program rather than about the size — so drawing the first frame of
an edited patch costs what a resize costs, and the planes start from nothing
there as they do after a resize.

**The clear behind a patch with planes is transparent rather than opaque black.**
One clear reaches every attachment, and a plane in the alpha channel of its
target would otherwise begin holding one. Nothing reads the picture's alpha — the
blit takes its three channels and the shader writes 1.0 into the fourth on every
frame it draws.

**What this does not do.** Feedback within a frame is still beyond the renderer,
which is what [0012](0012-feedback-as-a-module-not-a-cycle.md) closed on: a plane
is one step per frame, and an iterative solver wants several. Scan-order memory
stays unavailable, so a Delay and a Filter still hand their input through on the
picture and there is still no horizontal smear. And nothing here makes the video
path stateful in general — the state is exactly the planes, the ops that touch
them are exactly the breaker's two, and every other op in the instruction set
remains the pure function of its inputs that
[0064](0064-a-pixel-runs-only-what-a-pixel-changes.md) reorders.
