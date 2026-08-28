# ADR-0049: Record the GPU frame, not the interpreter

**Status:** Accepted · 2026-08-20 · *(user-directed)* · builds on
[0035](0035-a-glsl-backend-for-the-video-path.md) and
[0036](0036-export-video-as-motion-jpeg-in-an-avi.md), amends
[0018](0018-never-render-frames-on-the-ui-thread.md) and
[0024](0024-audio-device-in-the-shell.md)

## Context

The instrument could write a file and could not record a performance, and those
are not the same thing.

`MovieRenderer` takes a snapshot of the patch and evaluates it frame by frame
through `SynthRenderer` — the interpreter, once per pixel, on the processor.
Two things follow. The patch is frozen, so a knob turned during an export
changes the next export and not that one; and the cost is the one
[0035](0035-a-glsl-backend-for-the-video-path.md) built the shader backend to
escape, so it cannot run at any useful size in real time.

Both are correct for an export. An export should be deterministic and should not
depend on what the window was doing. But the whole point of turning knobs is that
the sound and the picture evolve together, and none of that is recoverable
afterwards: the patch file holds where the knobs ended up, not the path they
took.

The frames that show the performance have already been drawn. They are on the
card, at the preview's resolution, thirty or sixty times a second.

## Decision

**A recording reads the frames the GPU drew, and the samples the speakers got.**

Two paths, chosen by the file's extension the way an export already is —
`RecordKinds` mirrors `ExportKinds`, minus the still, because a still is not a
recording.

**The picture comes back through a pixel buffer.** `GpuReadback` issues
`glReadPixels` into one of two buffer objects and maps the other, so the frame
handed on is one behind the screen and the pipeline is never stopped to wait for
the card. `GlInterface` carries no `glReadPixels` — Avalonia binds what Avalonia
draws with, and it never reads back — so the three entry points come through
`GetProcAddress`, which is the same door Avalonia's own GL 1.0 bindings come
through. Where buffers cannot be mapped the read is direct and stalls, which is
worse but is still a recording.

**The read happens before the blit**, from the offscreen framebuffer rather than
from the control. A recording wants what the patch drew, not a letterboxed
corner of whatever shape the window happened to be.

**The frame is resolved to eight bits first, on the card.**
[0012](0012-feedback-as-a-module-not-a-cycle.md) is why the history pair is
`RGBA16F` wherever half floats are renderable, and reading a float surface as
bytes is not a combination `glReadPixels` is obliged to accept — ES answers
`GL_INVALID_OPERATION`, writes nothing, and leaves a buffer of zeroes. So every
read goes through an `RGBA8` target the frame is blitted into: the pair is then
legal whatever the history is, the card does the conversion, and the transfer
stays at four bytes a pixel instead of sixteen.

**Every read is followed by `glGetError`.** This is the decision that cost the
most to learn. A refused readback writes nothing and says nothing, and a buffer
of zeroes is indistinguishable from a patch that drew black — so without asking,
the failure mode of this whole feature is a file full of black frames and no
reason for them.

**The sound is the clock.** `AudioEngine.Fill` copies its buffer into a
lock-free ring after rendering it, so what is recorded is what was heard rather
than a second evaluation that would drift the moment a knob moved between the
two. The take's length is then a sample count, which is exact, and
`CapturePacer` asks that clock what the file owes.

**Falling behind means a repeated frame, never a stalled picture.** The preview
drops frames to hold its clock and an AVI is constant-rate. When the pacer says
two frames are due and only one was drawn, the last one is written twice. The
alternative — a frame per frame rendered, at whatever rate that turned out to be
— produces a file that drifts out of sync with its own audio, which is the one
thing a recording of a performance cannot do.

**Nothing is written until the first frame is in hand.** A file that opens with
the sound already running and the picture arriving a moment later is out of step
for its whole length.

## Consequences

**Only the GPU path can record.** `PreviewHost.BeginCapture` refuses on the CPU
renderer and says why. That is not a gap to be filled later: the interpreter is
too slow to be worth recording, and a slideshow with the right timestamps is not
a better answer than no.

[0035](0035-a-glsl-backend-for-the-video-path.md) allowed the two backends to
differ in their last bits and named the CPU renderer as the reference semantics.
This is the first place that choice is *load-bearing rather than tolerated*: a
take is the screen's truth, and the screen is the shader. Two recordings of the
same patch on two machines may differ where two exports would not, and for a
recording of a performance that is the right way round.

**Sound-only takes need no GPU and no frames.** A WAV goes through the ring and
`WavStreamWriter` and touches nothing else, so it works on either backend.

**`WavWriter` grew a streaming counterpart.** Its 44-byte header carries both
sizes, so it needed the whole take up front; a recording has no length until
somebody stops it. `WavStreamWriter` writes the header claiming nothing and
patches it on close — the trick [0036](0036-export-video-as-motion-jpeg-in-an-avi.md)
already documents for AVI. Both lay the header down through the same method and
convert through the same `ToPcm16`, so an exported file and a recorded one are
the same bytes for the same samples.

**[0018](0018-never-render-frames-on-the-ui-thread.md) gains a second thread to
stay off.** The render thread does one copy into a three-buffer mailbox and the
sound callback does one copy into a ring; the color conversion, the JPEG and
the file are all on the recorder's own thread. Neither realtime thread can be
made to wait by a slow disk, and neither allocates.

**The surplus is dropped where it is cheapest.** The mailbox is one frame deep
and newest wins, and the pacer is asked what is due *before* a frame is
collected — so a preview running at sixty into a file wanting thirty converts
and encodes thirty, not sixty.

**The JPEG encoder is the remaining ceiling.** It is scalar and single-threaded
and holds 960×540 comfortably; 1080p will repeat frames rather than keep up. The
pacing policy makes that degrade into a slower-looking picture instead of a
broken file, and the escape hatch — frames are independent, so encoding can be
spread across workers behind a reordering buffer — is available without changing
anything else here.

**The resolution picker is disabled during a take.** The header has committed to
a frame size and cannot be talked out of it. A frame arriving at another size is
ignored rather than trusted.

**A take that loses its renderer is finished, not abandoned.** The GPU failing
over to the processor mid-recording stops the take and says so; what is already
written is a recording, and what would follow is the same frame for ever. Closing
the window does the same, because a file whose header was never patched is not a
video.
