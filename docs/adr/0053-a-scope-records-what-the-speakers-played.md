# ADR-0053: A Scope records what the speakers played

**Status:** Accepted · 2026-08-25 · *user-directed* · gives up per-sink dead-code
elimination [0022](0022-one-output-per-sink-and-compile-per-sink.md) established,
for one module · answers the limit
[0040](0040-a-probe-is-a-second-compile-root.md) recorded about what a chart can
show

## Context

The Probe is a second compile root, not a second machine. It draws a signal by
*recomputing* it at every column, which is what lets it show the future as
readily as the past — and is also why it draws an oscillator without its
accumulated phase and a delay line as a wire. The video path evaluates pixels in
parallel and passes no state (ADR-0030), so there is nothing for a chart drawn
that way to remember.

Every limit anybody has run into with it is that one limit. A Sample's trigger
cannot be charted, because a trigger is something that happened before now. Nor
can an envelope that was fired, a reverb tail, a filter settling, a Sample & Hold
holding. 0052 wrote the wrinkle down for the Sample and left it there; the
question that followed was the right one — *the Probe cannot show the future of a
trigger, but in principle it could show what is already played?*

It could, and nothing about the Probe can. The past being asked for is not a
value the video program can compute; it is a value the *audio* program computed,
half a second ago, and threw away.

## Decision

**A separate module.** "Probe = what the eye computes, past and future" stays
exactly true, and "Scope = what the ear actually did, past only" is its own
honest thing. Put both on one node and they disagree wherever the patch has
memory; that disagreement is information, and a mode switch would have hidden it
behind a claim that the same chart was being shown two ways.

**A tap is an op, and the only op whose purpose is outside the program.**
`OpCode.Tap` writes one evaluation into a ring and produces no register. Every
other stateful op writes so the *next* evaluation can read — a delay line, an
accumulator, a cell. This one writes for something that is not the program at
all.

**The rings live on `DelayState`.** They are the one thing in there that nothing
in the program reads back, which is a real inconsistency and worth the trade: it
is the object the audio path already carries, and it is per-run rather than
per-program in exactly the way everything else there is. A recompile that changes
the shape of a patch drops the past along with the delay tails, which is the same
answer 0018 already gives.

**A Scope's input is a root of the audio program.** This is the part that gives
something up. Nothing downstream reads a Scope — its output is a colour — so the
walk back from the speakers would never visit what it is looking at, and the
chart would be of a signal that was never evaluated. So the compiler roots at
every tap as well as at the sink. `NodeDef.TapsSignal` declares it, rather than
the compiler knowing the module by name, so a plugin can want it too.

That is per-sink dead-code elimination deliberately abandoned, and it is
abandoned narrowly: only in the speakers' program, only for the input of a module
somebody placed on purpose, and only while it is there. Deleting the Scope
restores the elimination exactly.

**The chart is a table read.** The buffer the screen draws from is a
`LoadedSample`, read by the same `OpCode.Table` a Sample uses. So the module
drawing it knows nothing about rings, threads or sound cards, and shares its
whole picture with the Probe — `Charted` is one function, called by both, which
is what keeps a chart of the past and a chart of the future comparable when laid
side by side.

**The buffer holds exactly the window, whatever the window is.** The refill
stretches the asked-for stretch of the past across a fixed 2048 points, so the
program's job is only to say how far across the picture a column is. `window`
therefore appears in none of the module's ops: turning it changes what is put in
the buffer, not what is done with it, and nothing recompiles.

**The two programs are paired by node id.** They are compiled separately and throw
away different dead code, so a Scope's position in one has nothing to do with its
position in the other. `CompiledPatch.Taps` carries the ids, and
`Traces.Refresh` — called once a frame, from the same tick where the audio clock
is read — copies each ring into the buffer its own node's chart reads.

**A column is the peak of its bucket, not the mean or a sample of it.** A window
is always more evaluations than there are columns: a fiftieth of a second is four
thousand of them, two seconds is four hundred thousand. Taking one per column
aliases, and a tone whose period happens to divide the step charts as a straight
line — the one failure a scope must not have. Averaging is worse at the far end:
a waveform is symmetric about nought, so a bucket holding whole cycles averages
to nothing and two seconds of a loud tone draws as silence. The peak has neither
problem, and a bucket of one evaluation is that evaluation, so a chart that did
not need decimating is untouched.

**Reading may tear, and that is the trade taken on purpose.** The ring is written
by the sound callback and read by the thread that draws. A chart with a seam in
it for one frame is a better answer than a lock on the audio thread — the same
trade `AudioRing` already makes for a recording.

## Consequences

**The Scope has three cliffs, which are one cliff.** It shows nothing until sound
is switched on. It shows nothing the Output's `left` and `right` do not reach,
since a branch that only draws was never played. And it shows only the past. All
three are the same fact — it is a record of what happened, not a computation of
what would — and the module's own description says all three rather than letting
somebody discover them as bugs.

**A Scope on screen gives up the shader,** through the mechanism 0052 already
built rather than a new one: its program carries a table, and a program carrying
a table is drawn on the processor. Nothing new was needed, which is some evidence
that mechanism was drawn at the right level.

**The chart holds its last sweep while sound is off,** rather than blanking. That
is what a scope with the beam stopped looks like, and it makes the freeze useful;
a rewind clears it, because the timeline it was a picture of no longer exists.

**Two megabytes per Scope.** Two seconds at the oversampled rate, in float. It is
allocated when the patch has one and not otherwise, and it is the reason the ring
is a fixed length rather than sized from the window — a knob that reallocated the
audio path's memory as it turned would cut every delay tail in the patch on the
way past.

**`DelayState` now holds something write-only.** Its remarks say so plainly. If a
third thing of that kind ever turns up, the class has stopped being "what a
program remembers" and become "what a run carries", and it should be renamed
rather than have the definition quietly widened again.
