# ADR-0174: A polyphonic wire is lowered once per voice

**Status:** Accepted · 2026-10-05 · *user-directed* · builds on
[0062](0062-indexed-polyphonic-midi-voices.md), which declined copying a chain
per voice for MIDI alone; implemented in `VoiceCounts` and `PatchCompiler`

## Context

[0062](0062-indexed-polyphonic-midi-voices.md) made MIDI polyphonic by placing a
MIDI In per voice, so a four-note chord through a filter and an envelope was four
copies of the filter and the envelope, wired by hand and edited four times. A
wire that carries one signal per voice lets one chain play the chord.

## Decision

**Polyphony is a property of a wire, not a new `PortKind`.** A module that starts
a polyphonic wire says how many voices it starts, up to eight; every module it
reaches runs that many, and a wire carries the count of the module it leaves.
`VoiceCounts` works the counts out for the compiler and the canvas alike. The
kinds stay `Scalar`, `Color` and `Any`, and nothing in the plugin contract moves:
`NodeDef.StartsVoices`, `NodeDef.MergesVoices` and `EmitContext.Voice` are
internal.

**The compiler expands, the patch does not.** Resolving is keyed by module and
voice, so a module on four voices is lowered four times, each lowering claiming
memory of its own: a phase, a delay line and a cell per voice. The backends see
an ordinary program, so the IL, GLSL and JavaScript agree with the interpreter as
[0035](0035-a-glsl-backend-for-the-video-path.md) has them do on everything else.
A loop round a polyphonic chain keeps a plane per voice.

**Where counts meet, the larger runs, and a missing voice is silent.** A wire of
two voices into a module on four reads nought in voices 2 and 3, so a merge never
counts voice 0 twice. A wire of one voice reaches every voice the same.

**A module that must hear every voice at once has them added in front of it.**
The Output, the Merge, the Reverb, the Probe, and anything that taps or charts a
signal (Scope, Analyzer, Meter, a plugin's meter) sum the voices arriving and run
once. Sound and picture merge the same way, by adding. Delay lines, filters,
envelopes and strings are copied per voice, so a plucked string plays a chord.

**A plugin's module is copied like a built-in one.** Its emit function writes
ops and its memory is claimed from the emitter, the same as the engine's, so
lowering it once per voice gives it memory per voice with nothing declared. The
handoff's plan to merge in front of every plugin module was made before that was
read, and is not needed. A plugin that wants to sum its voices, or to know its
voice, would need the two flags made public, which moves the contract's minor;
left until one asks.

**The sources.** MIDI In gains a `voices` field: at N it plays voices `voice` to
`voice + N − 1`, or 1 to N when `voice` shares them out, never past the eighth, so
the hub's allocation over the voices a patch reads (0062) carries on unchanged.
Voice (`poly.voice`) is a wire of its voice numbers, 0 up, for a pitch, a seed or
a hue; a Wander or a Noise seeded by it walks differently per voice. Spread
(`poly.spread`) turns one wire into N, a step of `offset` apart. Merge
(`poly.merge`) adds a wire's voices into one. Sound and picture share the voice
index, so voice 3's pitch and voice 3's ring are one voice.

**The canvas draws a polyphonic wire as a double line with its count.** That is
presentation; nothing in the patch file marks a wire polyphonic.

**Stereo stays two sockets.** `left` and `right` are identities and are never
summed into each other; each may carry voices, and the Output sums within a side.

## Consequences

A chord costs what N copies of the chain cost, and nothing downstream of a Merge
is copied. Every module lowered per voice repeats its compile issues, which are
said once.

Drum tracks as a source, a voice count chosen at run time, and an `over` merge for
the picture, which needs coverage, are not built.
