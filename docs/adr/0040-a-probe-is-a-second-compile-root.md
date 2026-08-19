# ADR-0040: A probe is a second compile root, not a second machine

**Status:** Accepted · 2026-08-19 · *user-directed* · extends
[0011](0011-compile-backwards-from-output.md) with a root that is not a sink

## Context

Nothing in this instrument can be looked at. A patch is a graph of modules whose
values are only ever seen through the one window at the end of it, and a wire
carrying the wrong number looks exactly like a wire carrying the right one until
the picture is wrong — at which point every module upstream is a suspect. The
usual answer in a modular rig is an oscilloscope: one input, and a drawing of
what arrives at it.

The obvious way to build one here is the wrong shape for this program. A scope
measures: it samples a signal over time, keeps the samples, and draws them. That
means a path out of the render loop, a buffer somebody owns, a decision about
which thread fills it and a second drawing surface in the shell to put it on —
and none of it would work on the GPU backend
([0035](0035-a-glsl-backend-for-the-video-path.md)), where the values exist only
inside a fragment shader and never come back.

But a chart of a signal is a picture, and this program makes pictures out of
functions of `(x, y, t)`. The value at this column, against the height of this
row, is such a function. Nothing has to be measured, kept or read back — which
leaves exactly one thing that is genuinely hard: the value at *this column* is
the signal at a different moment from the value at the next one, and a program
evaluated once per pixel has one `t`.

## Decision

**The Probe is an ordinary module, and its chart is ordinary ops.** It declares
one input, two knobs and a colour output, and it lowers to about forty
instructions of arithmetic — a trace, a fill, a grid and a marker for a value off
the top of the scale. Nothing in either renderer knows it exists; the GLSL
backend draws it because it is the same op list as everything else.

**The screen is compiled from the probe rather than from the Output while one is
selected.** `CompileForProbe` roots the walk at the probe node, which makes the
patch's own picture not merely covered up but never lowered: the Output is not
reached, so nothing upstream of `colour` emits an op. That is
[0011](0011-compile-backwards-from-output.md) used exactly as written, with a
root that happens not to be a sink. The speakers are unaffected — they root at
the Output whatever the screen has been asked for, so a patch can be heard while
a chart of one corner of it is read.

**An input may be swept, which is how the moment varies along the picture.** A
`PortSpec.Swept` input is not resolved before its module is entered. The compiler
hands the module a resolver instead, and the module calls it after pushing a
domain onto the emitter: `Emitter.PushDomain` substitutes what `LoadX`, `LoadY`
and `LoadT` hand back, so every Coordinates and every Time lowered inside the
sweep reads the probe's time-across-the-picture rather than the frame's clock.
The substituted registers are ordinary registers. Both backends run the result
without knowing a domain was ever swapped.

**Selection is the switch.** A chart is something you look at, not a state an
instrument is left in: clicking the module shows it, clicking away puts the
picture back, and neither the patch nor the file changes. The status line says
which of the two is on screen, because a probe left selected otherwise looks
exactly like a patch that has stopped working.

## Consequences

**The chart cannot show memory, and this is the real limit.** The video path
evaluates pixels in parallel and passes no state, so an accumulated phase is the
multiply it replaces and a delay line is a wire
([0027](0027-delay-lines-give-the-audio-path-a-memory.md),
[0030](0030-oscillators-accumulate-their-phase.md)). A probe therefore charts
what the *screen* makes of a module, which for maths, coordinates and an
oscillator at a steady pitch is the same thing the speakers hear, and for a swept
pitch or a reverb tail is not. A true audio scope would have to be driven by
`AudioRenderer` with its own `DelayState` and drawn in the shell — a different
decision, deliberately not taken here.

**x and y are pinned inside the sweep.** A module read with Coordinates has a
value per pixel and there is no one line that is all of them, so the probe charts
the signal at the middle of the picture. Charting a field would be a second mode
drawing false colour rather than a trace, and it would need the unswept domain;
one module doing both would lower its input twice.

**A module read inside a sweep and outside it is lowered twice.** The two
readings are at different moments, so they cannot share a register, and the
sweep resolves under a cache of its own. In the ordinary patch — a probe hung
off one output, watching — nothing is shared and nothing is duplicated. Feeding
the *same* module into both a probe's `in` and one of its knobs is what pays,
and it pays in ops rather than in correctness.

**The compiler now has a root that is not the sink, and says so in two places:**
the port range that splits the screen from the speakers is skipped, since there
is no Output in the program to split, and "nothing is wired in" is said about the
probe instead. A stale probe id — a selection outliving the module it named —
compiles the picture rather than a black screen.

**The timebase is a decade knob, which is a third display kind.** Five decades
separate a chart of an audible tone from a chart of an LFO, and no linear slider
spans them: at a maximum of thirty seconds every audio-rate setting sits inside
the first thousandth of the travel. So `window` holds the power of ten and
`PortDisplay.Duration` writes it out as the time it is — the same trick
`PortDisplay.Note` already plays with a note number, through the same hook, and
the cost is the same one: the number in the box is not the quantity, so the
inspector needs the column that says what it means and the assistant's handbook
has to name the unit. Two ops at the module, and a signal patched into the socket
sweeps the chart exponentially, which is the only way a sweep across that range
is any use.

**The trace breaks into dots where the signal moves faster than a pixel column.**
There is no derivative available to widen it with and no second evaluation worth
paying for, so the fill under the trace carries the reading instead: at a sweep
too slow for the waveform the chart becomes the envelope band, which is what an
oscilloscope shows in the same situation and for the same reason.
