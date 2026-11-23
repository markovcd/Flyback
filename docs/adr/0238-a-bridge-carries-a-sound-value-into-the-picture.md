# ADR-0238: A Bridge carries a sound value into the picture

**Status:** Accepted · 2026-11-23 · *user-directed* · extends
[0058](0058-the-picture-is-told-how-loud-the-sound-is.md)

## Context

A module with memory (an envelope, a slew, a hold, a feedback counter) means
something only in the speakers' program; the picture gets something simpler
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)'s `ModuleSinks.Audio`).
The one way across was a Meter, and a Meter reads loudness. A real user's live
visualizer showed what that costs: every envelope went through
`meter(window: 5ms)`, everything signed was lifted by 2 first so the root mean
square would not fold it, and every phase and counter was sent as a sine and a
cosine and put back together with `atan2`, because a phase crossing its top
averages to the middle of its range and the picture jumps once a turn.

## Decision

**A Bridge module, tapped exactly as a Meter is.** Its input is swept and a root
of the speakers' program; its readings are played in once a frame as live values
under the Meter's key prefix, so every host that already refreshes Meters (the
editor, the viewers, renders, stills, the web page) carries Bridges with no
change of its own.

**What it reads is the value, not its loudness.** `value` is the mean over
`window` (about a frame by default), so a sign survives and a steady value reads
as itself. `top` is the highest the window got, which is how a trigger one
evaluation long is seen at all.

**`wrap` makes the mean a circle's.** Set to a phase's or a count's period, the
window's values are averaged as angles and read back between nought and `wrap`,
which is what the sine and cosine pair did by hand. Both knobs are read at
compile time, as a Meter's window is, because the reading is made outside the
program.

## Consequences

A patch carries an envelope, a count or a phase across in one module, and the
picture keeps the shader: a uniform per reading and none of the signal chain.

The reading is a frame old and a frame coarse, like a Meter's: right for a
picture, not for audio-rate work. The speakers' program reads a Bridge the same
way, a frame late.

**Declined: bridging automatically.** The compiler could insert a Bridge
wherever the picture reads a module with memory. It would change what every
existing patch draws, and it would be wrong wherever that module's input varies
across the picture: a Filter over a pixel signal is a wire on the screen today,
and a bridged one would be a single number. Placing a Bridge says which values
are the sound's.
