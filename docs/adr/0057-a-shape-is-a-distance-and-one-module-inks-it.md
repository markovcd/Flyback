# ADR-0057: A shape is a distance, and one module inks it

**Status:** Accepted · 2026-08-27 · *user-directed* · follows
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)

## Context

Every module the picture had was an infinite field. Coordinates, Time, the five
oscillators, Noise, Checker and Rings all go on for ever in every direction, and
the eight modules under Space bend the plane those fields go on for ever across.
So a patch could make a texture of any kind at all and could not make a *thing*.
There was no circle in the catalogue. The nearest a patch could come was a Length
into a Threshold — a disc with a hard edge, three modules deep, that nobody
finds and that stair-steps when they do.

The count says it plainly. Four plugins had been written and all four are for the
ear: a filter, a wavefolder, a saturator, a delay, a reverb, a chorus, a flanger,
a phaser and a supersaw. The eye had three Patterns, five Colors and whatever
the maths could be persuaded into, and had had exactly that since the first
week. This is the same gap
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) found on the other
side of the machine — a catalogue strong on where a signal goes and empty on what
it is when it gets there — arriving a second time on the half of the instrument
that gave the program its name.

## Decision

**A shape is a signed distance, not a mask.** Each of the four form modules hands
out one number per point: negative inside, zero on the edge, positive outside,
in the units the Coordinates module already uses. A mask — one inside, nought
outside — is the obvious output and is a dead end. Two masks cannot be combined
into anything but a fade, an outline cannot be recovered from one, and a mask
does not say how far away the edge is, which is the number every soft edge is
made of.

**What that buys is that the catalogue already had the combinators.** Union is
the smaller of two distances and intersection the larger, which is to say
Minimum and Maximum, which have been in Maths since the first week. Growing a
shape is subtracting from it. An outline is the distance to the edge with its
sign thrown away. None of that needed a module, and the two that did are the two
that could not be assembled: a Combine, whose seam melts the crease a plain
Minimum leaves where two forms meet, and a Fill, which is where a measurement
becomes something to look at.

**One Fill rather than a fill knob on every shape.** The decision "what does this
distance look like" is taken once for a whole assembly of forms — a Combine of
four shapes is one Fill — rather than four times, slightly differently each time.
It hands out the form and its own edge together, for the reason the Filter hands
out three responses at once: they are two readings of one number and a patch
usually wants both.

**Sizes are in the picture's units, and there is no pixel anywhere.** Softness,
outline width and corner radius are all fractions of the frame, because nothing
in a compiled program knows how large the frame is — the same patch is drawn at
preview size, at export size and into a movie
([0035](0035-a-glsl-backend-for-the-video-path.md)). A softness of a hundredth
is three pixels on a 540-line preview and six on a 1080-line render, which is
what makes a still and the preview of it the same image.

**Six modules and no engine change.** Not one opcode, and not even the
one-evaluation cells [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)
hands out. Everything here is arithmetic over x and y.

## Consequences

**A video plugin has a gate an audio plugin does not, and it is now written
down.** A module that reads a table — `em.Table`, which is a clip or a trace —
still compiles and still draws, and takes the preview off the shader for as long
as that patch is loaded, because a table is the one thing the GLSL backend cannot
draw at all
([0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) records the
same price being paid by the sample player, and names the texture upload that
would end it). Nothing warns anybody, and on the audio path there is nothing to
warn about, which is why four plugins went by without the question coming up. The
state-carrying ops are milder: a cell or a delay line lowers to its no-state
fallback, which is exactly what the interpreter does on the video path, so the
two backends still agree. The test that every module here lowers to every GLSL
dialect, and that none of them reaches for a table or a cell, is what stands in
for the complaint the shader cannot make.

**The forms are exact, and one of them is not.** The Star measures to a segment
with ends on it and is a true distance everywhere — its slope is one, which the
tests measure. The Polygon measures to the nearest edge's *plane*, so beyond a
corner it reads a little less than the truth. That is invisible in a fill and
matters to anything that dilates a field by a large amount, which is the one
thing this convention makes easy enough to try.

**Counts step rather than slide.** A polygon of five and a half sides has a seam
where the fold does not close, so 'sides' and 'points' are floored. This is the
opposite of what the Kaleidoscope does with its 'segments', and the two are right
for opposite reasons: that module folds the plane and does not care whether the
wedge comes back to where it started, and these have closing as their whole job.

**The fold costs the same whatever it draws.** A polygon is the intersection of
as many half-planes as it has edges, which written out would be one Max per side
and a different program per count. Folding the bearing instead makes it fifteen
ops for a triangle and fifteen for a sixteen-sided one — and makes the count a
signal, which the straight-line register machine could not otherwise have
allowed.

**The category is "Form", because "Shape" was taken.** Timbre's Fold and Drive
shape a *signal*. Two senses of one word, one palette, and the collision is worth
a slightly stiffer name.

**A shape can be heard, and that is what the preset is.** The Scan
([0043](0043-a-scan-is-a-probe-read-backwards.md)) sweeps a loop through the
field and hands what it passes over to the speakers, so a star's five points are
five bumps in every cycle of the waveform and sharpening them brightens the tone.
Nothing here has a memory, so both sinks run the same arithmetic and the sound is
the shape rather than something chosen to go with it — which is the property
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) exists for, arriving
somewhere it could not before.

**What is still missing from the eye is larger than what arrived.** One octave of
noise and no fBm, no Voronoi; five colour modules and no palette, no RGB back to
HSV; a Feedback that can be sampled anywhere and nothing built on that; and no
way at all to read a picture in. The first of those is another plugin of pure
arithmetic. The last is an opcode, and
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) already
argues it: a clip uploaded as a texture would work on both backends, since
`SampleFeedback` proves a texture read lowers to GLSL.
