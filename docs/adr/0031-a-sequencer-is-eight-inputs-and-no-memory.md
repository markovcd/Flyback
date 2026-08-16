# ADR-0031: A sequencer is eight inputs and no memory

**Status:** Accepted · 2026-08-16 · *user-directed* · extends
[0009](0009-editable-defaults-on-every-input.md) and
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)

## Context

The instrument could make a sound and draw a picture, but it could not play a
tune. Pitch came from a knob, or from a ramp through a
[Note](0030-oscillators-accumulate-their-phase.md) — a run up the chromatic
scale, which is every note in order and not a melody. Choosing eight notes and
their order needed something new.

Two questions had to be answered before any of it could be written, and neither
was about sequencing.

**Where do the steps live?** A sequencer holds a pattern, and nothing in the
machine holds a list. A module is one `NodeDef` pairing sockets with the ops it
lowers to ([0008](0008-modules-as-data-in-one-catalogue.md)); an instance is a
`float[]` of knob values, one per input
([0009](0009-editable-defaults-on-every-input.md)); a file is those two things
in JSON ([0020](0020-json-patch-files-keyed-by-string-type-ids.md)). A pattern
of arbitrary length fits none of them without a new kind of storage running the
whole width of the program — a port kind, a serialiser, an inspector control and
a compiler path.

**What does it remember?** [0027](0027-delay-lines-give-the-audio-path-a-memory.md)
gave the audio path state, and [0030](0030-oscillators-accumulate-their-phase.md)
put an accumulator in every oscillator. Both are identified by position among
the stateful ops, both are lost on a recompile that changes their number, and
both are silent on the video path. A sequencer is obviously the same kind of
thing — it steps, so something must be counting.

## Decision

**The steps are ordinary inputs.** Eight of them, each paired with an `on`
knob, on a module like any other. Nothing is added to the format, the
inspector, or the compiler.

**It has no memory.** Which step is playing is a function of where the input has
got to, not of what played before:

```csharp
var travelled = em.Mul(i[0], i[1]);                              // in x rate, counted in steps
var index = em.Unary(OpCode.Floor, em.Binary(OpCode.Mod, travelled, count));
```

**Selection is a sum of windows, because the machine has no branches.** Each
window is the difference between two thresholds on the index, so adjacent
windows share an edge, exactly one is ever open, and the sum is the step that
one selects.

**`in` is a domain, not a clock** — [0030](0030-oscillators-accumulate-their-phase.md)'s
rule, for the same reason. Modulo is floored, so an input running backwards runs
the pattern backwards rather than falling off the front of it.

**Two modules, one emit function.** `Note Sequencer` shows a step as `A3` using
the display [0030](0030-oscillators-accumulate-their-phase.md)'s Note module
already had; `Sequencer` shows the same knob as a number. Nothing below the port
declarations can tell them apart.

**Three outputs, because the two sinks want different things.** `out` is the
step's value, `gate` is its `on` shaped by a gate length, and `index` is how far
through the pattern the sequence has got.

**The gate ramps rather than switches.** A switched gate steps the amplitude by
its whole height in one sample, and
[0023](0023-oversample-the-audio-path.md) band-limits a discontinuity rather
than removing one — so the first version of this module clicked at every note,
measured at nineteen times the wave's own sample-to-sample travel. The edges are
a `shape` knob given as a fraction of a step, so they scale with the tempo, and
they are floored at two thousandths of a step so that a knob turned to nothing
cannot put the click back.

## Consequences

**A step is a socket.** Because the steps are inputs and inputs take wires
([0009](0009-editable-defaults-on-every-input.md)), an oscillator can be patched
into step 3 and that one note drifts while the other seven hold. A stored array
of numbers could not have done that, and it is the more interesting instrument
for it — a pattern that is partly fixed and partly alive is a thing this could
not previously express at all.

**Old files still open, and future ones will.** A patch is written and read with
no change to `PatchIo` whatsoever, and
[0020](0020-json-patch-files-keyed-by-string-type-ids.md)'s trailing-default
fallback means a ninth step could be added later without invalidating a single
saved file.

**It costs the same in both programs, and needs no `DelayState`.** This is the
first module since the delay lines that could plausibly have wanted state and
turned out not to. A patch of nothing but sequencers and oscillators declares
the accumulators the oscillators need and no lines at all, and a recompile that
disturbs the phases leaves the sequence exactly where it was — there is nothing
in it to disturb.

**Eight is a decision, not a limit that fell out of anything.** It is what fits
on a node without the inspector becoming a list to scroll, and two chained
through one another is sixteen. Sixteen in one module would have been a node
about twice the height of any other in the catalogue.

**Elimination is per module, so every patch pays for all three outputs.** A
sequencer driving only a pitch still emits the window sum for the gate — about
fifteen ops of the sixty. This is the same trade
[0011](0011-compile-backwards-from-output.md) already makes for Coordinates,
which computes a radius and an angle nobody asked for, and it is affordable for
the same reason.

**Sixty ops per pixel is not free.** Nebula is seventy-nine and renders, so a
sequencer in a patch is comparable to the most expensive preset here — but
unlike Nebula it is doing identical work at every pixel whenever its input is
`t` rather than a coordinate. The machine has no notion of a value that is
uniform over a frame, and this is the first module where that costs something
visible. Hoisting per-frame invariants out of the pixel loop would fix it, and
would be a change to the compiler rather than to this module.

**There is no glide, and adding one is not cheap.** A slide between steps means
either selecting twice and mixing — which doubles the module — or overlapping
the windows, which breaks the property that they sum to one and needs
normalising. Both are real options; neither is a small edit, and a hard-stepped
sequencer is the correct default in any case. The gap
[0030](0030-oscillators-accumulate-their-phase.md) named when it removed
`glide` from Note is therefore still open.

**`on` is a level, not a switch.** It multiplies rather than thresholds, so a
step at 0.4 is a quiet one. That makes it a velocity for free, and it means
"rest" and "quiet" are the same control rather than two.

**[0030](0030-oscillators-accumulate-their-phase.md) solved half of this
problem, and the half it solved was the harder one.** A stepped pitch needed an
accumulator and a new opcode; a stepped amplitude needs an envelope, which is
four ops and no state. But the two failures sound identical from the listening
position, and only one of them had anything in the codebase to catch it. The
envelope also earns its keep at the step boundary: because it reaches zero
there, a step whose `on` differs from the last one's fades in at its own level
instead of jumping to it, so the one remaining discontinuity in the module is
multiplied by nothing.

**Nothing measures clicks except a test that goes looking for one.** The tear
was found by the measurement
[0030](0030-oscillators-accumulate-their-phase.md) introduced — largest
sample-to-sample step against the wave's own travel — pointed at this preset,
and it read 18.8×. The gate envelope brings it to 1.2×. That measurement is
worth pointing at any module that multiplies a signal by something which
changes, because the status bar cannot report a click and neither can a
snapshot.
