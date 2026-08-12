# ADR-0029: Linux sound through ALSA, on a thread of our own

**Status:** Accepted · 2026-08-12

## Context

[0025](0025-platform-io-behind-loadable-plugins.md) said sound on Linux was a
project someone could add without touching, or rebuilding, the shell.
[0028](0028-publish-one-platform-at-a-time.md) did it for macOS and proved the
claim. Linux is the last platform, and the only one where the question "which
API" has more than one defensible answer.

PipeWire is what a current desktop actually runs, and its native API is the one
with a future. PulseAudio is what the previous decade ran and is being replaced
by PipeWire, which impersonates it. ALSA is underneath both, is in the kernel,
and is the only one of the three that is present on a machine with none of the
others.

The deciding fact is what happens to the `default` device. PipeWire and
PulseAudio both ship an ALSA plugin and both claim `default`, so an ALSA program
on a PipeWire desktop is a PipeWire client without knowing it. Choosing either of
the servers directly would mean *requiring* it; choosing ALSA means being routed
by whichever one is there, or talking to the card when neither is.

The second question is not about Linux at all. WASAPI and CoreAudio both call
*us* — a callback on a thread the system owns, with a deadline. ALSA does not.
`snd_pcm_writei` blocks until the card has room, so somebody has to be in a loop
calling it.

## Decision

**ALSA, through `default`, never `hw:0`.** Naming the card directly would take it
exclusively and silence everything else on the machine, which is not a thing a
patch editor should do. `snd_pcm_set_params` does the whole hardware and software
parameter negotiation in one call, with software resampling on, so the rate the
engine renders at is the rate it asked for.

**The plugin owns the audio thread.** `AlsaAudioDevice` starts a background
thread that fills a block from the same `AudioCallback` the other two backends
are handed, and writes it. The contract does not change by a word:
`AudioCallback` never said who calls it, and [0024](0024-audio-device-in-the-shell.md)'s
rule — do not block, allocate or throw — is exactly as binding when the thread is
ours. The block is allocated by `Start`, on the caller's thread, so the loop
never allocates.

Everything touching the handle happens on that one thread while it lives, because
alsa-lib does not promise otherwise. Stopping therefore asks the writer to finish
and waits for it, rather than reaching into a device another thread is inside. A
writer that has not returned in two seconds — a device that has stopped answering
— leaks the handle instead, because closing it underneath a thread still writing
to it would end the process rather than the sound.

**`IsSupported` asks two questions:** the operating system, and whether libasound
is on the machine at all. Containers and server installs routinely have no sound
library, and that is not a failure to report — it is the same "no" a plugin for
another platform gives. Asking costs a `dlopen` of a library that is not opened,
which is still short of ADR-0025's line about not touching a device.

**The shell survives a device that will not open.** A device is only really
opened by `Start`, which the Audio button called without a net; a busy card would
have taken the program down. It is now caught, reported through the same status
line as every other plugin failure, and the button goes back off and stays off.

## Consequences

Every platform the shell runs on can now make a sound, and the shell still
contains no code that knows what a platform is. Three backends, three folders
under `plugins/`, one contract, and the selection between them is still the
`Priority` and `IsSupported` pair from 0025 — all three claim 100, and no machine
supports two.

Latency is whatever `snd_pcm_set_params` grants for the 30 ms asked for, through
however many layers are between `default` and the card. On a PipeWire desktop
that is one more hop than a native PipeWire client would take. If it ever proves
to be too many, a second Linux plugin at a higher priority is the answer, and it
does not replace this one: this is the backend that works everywhere, including
where PipeWire is not.

The device is the least testable class in the repository, as its two siblings
are: it needs a sound card and a Linux machine. What is testable without either
is that the plugin loads on Linux, registers, and reports itself unsupported
where libasound is absent — which was checked on Ubuntu 24.04, from a
cross-published build, with no sound library installed.
