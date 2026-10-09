# ADR-0190: Linux sound goes through jackd when one is running

**Status:** Accepted · 2026-10-09

## Context

[0029](0029-linux-sound-through-alsa.md) chose ALSA for Linux because every sound
server there answers to it: PipeWire and PulseAudio both claim `default`, so ALSA is
routed by whichever is installed. What ALSA cannot give is what people who run a JACK
server want: ports other programs patch to, the server's clock instead of a writer
thread of ours, and a latency set once for the whole machine.

## Decision

**JACK is a second sound output in the Linux plugin, above ALSA.** `JackAudioOutput`
has priority 150 against ALSA's 100, so it plays whenever it is supported.

**Supported means jackd is running, not that libjack loads.** PipeWire implements the
JACK API too, and counting it would move every PipeWire desktop off ALSA, which follows
the system's default device. `JackServer.IsRunning` finds jackd's own socket
(`/dev/shm/jack_<server>_<uid>_0`) and connects to it, which also rules out the socket a
crashed server leaves behind. It registers no client, as `IsSupported` must not open
anything. `JACK_DEFAULT_SERVER` names the server, as it does for libjack.

**The server decides the rate and the block size.** A client learns the rate only once it
is connected, so the device connects when it is made and registers its two ports,
`out_left` and `out_right`, only on `Start`. Its `SampleRate` is the server's, and the
engine renders at that. Latency is one server period.

**JACK calls us.** The process callback fills the ports from the engine's interleaved
callback, in blocks no larger than a port buffer. A server that goes away clears
`IsRunning` and the next `Start` connects afresh.

**One setting, where the ports connect:** the first two physical playback ports by
default, or nowhere, for a patchbay to route.

## Consequences

PipeWire users stay on ALSA; JACK is for somebody who started jackd. MIDI and Line In
stay on ALSA, and JACK versions of them would be backends of their own.

The gate image carries `jackd2`, and the tests start a dummy-driver server of their own
when none is running.
