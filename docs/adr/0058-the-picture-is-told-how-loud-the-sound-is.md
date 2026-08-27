# ADR-0058: The picture is told how loud the sound is

**Status:** Accepted · 2026-08-27 · *user-directed* · follows
[0053](0053-a-scope-records-what-the-speakers-played.md), amends
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)

## Context

The premise of the program is one patch making a picture and a sound. The two
sinks have never disagreed about anything, because they are two readings of one
graph — and that is exactly why the eye could not hear the ear. Everything a
patch did to make them agree, it did by sending one modulator to both places: the
Drone's sweep, the Supersaw's detune, the Sequence's gate. A patch saying a thing
twice, rather than one half of it listening to the other.

[0053](0053-a-scope-records-what-the-speakers-played.md) got close and stopped
one step short. A Scope carries a stretch of the past out of the speakers'
program and into the screen's, and the screen reads it as a table — which is a
picture *of* the sound and not a number *about* it, and which costs the shader:
`Table` is the one op the GLSL backend cannot draw, so a patch charting sound
draws on the processor for as long as it is loaded
([0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) records the
same price for a sample). There was no envelope follower, no level, no band
energy. The Kick preset says the consequence out loud: its envelopes have no
memory on the video path, so what the screen gets of a drum is the gate — a flash
a beat, in time with the sound rather than shaped like it.

## Decision

**A Meter, and it is told rather than computed.** The module taps its input the
way a Scope does — the socket is a root of the speakers' program whether or not
anything downstream reads it, and it is `Swept`, so the screen never lowers the
signal chain behind it. What the screen gets back is two numbers, and it gets
them as **live inputs**: `OpCode.LoadLive`, the op a keyboard is played on. So
the picture does not work out how loud the sound is. Nothing in the program does,
and nothing could — a frame is one evaluation per pixel with no past to reduce,
and the past belongs to the other sink entirely. Something outside both programs
listens to the ring and plays the answer in.

**Which means no new opcode, and that was the surprise.** This was expected to
need one. It does not, and the reason is worth stating: `LoadLive` is already
"the one op whose answer comes from outside the program and from outside the
patch", `LiveValues` is already keyed by name because the two ends never meet,
and the shell already writes one value into two blocks by name so that a note
reaches both sinks. A level is that same shape — a number from outside, named,
arriving between frames. It is [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)'s
finding a second time: a mechanism built for one thing turned out to be the
general one, and the boundary somebody drew around it was narrower than the
thing itself.

**The window is the smoothing, and it is the only smoothing.** Level is the root
mean square of the last `window` seconds and peak is the furthest that stretch
got from nought — both off one pass, because they answer different questions and
a patch that wants the difference should not need two meters. There is no attack
or release: a shorter window is a faster meter. The knob is read at compile time,
exactly as the Scope's is and for the same reason — what fills the reading in
runs once a frame, outside the program, and cannot act on a value arriving per
sample. `scale` is an ordinary socket by contrast, because it is applied after
the number arrives, so it can be swept.

**Once a frame, which is all a frame can use.** The reading is taken on the same
tick the Scope's chart is refilled — the one moment in the loop where neither
path is running. A picture cannot show a level it was told about between two of
its own frames.

**Tapping and charting are now two questions.** `NodeDef.TapsSignal` still means
"my input is a root of the speakers' program". `ChartsSignal` is new and means
"and the screen reads that stretch back as a table". A Scope says both. A Meter
says only the first, and so is allocated no chart buffer and costs the refill
nothing.

**An export measures itself.** `MovieRenderer` now renders a frame's audio before
the frame rather than after, and takes the reading in between. The write order to
the file is unchanged and the sample arithmetic is unchanged, so every existing
patch exports the same bytes; what changes is that a frame can be lit by the
sound it is played with rather than by the sound before it — which the preview
cannot do, because on screen now is all there is.

## Consequences

**The picture keeps the shader.** This is the whole point of the shape and it is
what a chart cannot offer: two uniforms against a texture read. The approved GLSL
for the *Heard* preset has `uniform float uLive[2]` in it and no table at all, and
that snapshot is the artefact worth pointing at.

**And it costs the frame nothing else.** The input is swept, so the signal being
measured is never lowered into the picture's program. A patch whose hue follows a
bass line does not compute a bass line per pixel — it reads one number. The tests
pin that by putting nine oscillators behind a meter and counting the `sin` ops in
the frame, which is nought.

**A meter reads nothing where there is nothing playing.** A still has no past to
be loud in, and a patch drawn with the sound switched off is drawn with every
meter at zero. That is not a fallback but the fact, and it wants saying in any
patch built on one: the *Heard* preset keeps a floor under the reading so that
switching the sound off stops the picture rather than extinguishing it — the same
decision the Sequence preset made about its gate, for the same reason.

**It goes to zero when the sound stops, where a chart holds its last sweep.** A
scope with the beam stopped is a picture of the last sweep and reads as one. A
level frozen at whatever it was when the speakers went quiet is a lit picture
with nothing playing, and nothing about it says so. So the shell says it —
`AudioEngine.Deafen`, on the same toggle that stops the device.

**The reading is one frame old on screen, and a sidechain gets frame
resolution.** A level wired back into the sound moves sixteen milliseconds at a
time, which is right for a picture and coarse for a compressor. That is the
honest limit of a number that crosses between the sinks once a frame, and a
module wanting audio-rate envelope following should follow ADR-0041 and keep its
own cells; nothing here stops it.

**`EmitContext` gained the node id.** A module whose value is filled in from
outside has to be addressable by name, and the two programs of a patch share no
numbering — the same wall `TapSpec.Node` hit, answered the same way. It is
identity rather than carried state, so it does not touch what
[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) counts.

**The other half of this is still missing.** A level is one number about the
whole signal; band energy is several, and the picture that wants to know whether
the loud thing was a kick or a hat still cannot. What that needs is a filter bank
on the tap and three names rather than one, which is this record's mechanism
repeated and not a new one — which is the argument for having chosen a mechanism
rather than an opcode.
