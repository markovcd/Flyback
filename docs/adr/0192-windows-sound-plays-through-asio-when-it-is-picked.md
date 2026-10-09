# ADR-0192: Windows sound plays through ASIO when it is picked

**Status:** Accepted · 2026-10-09 · *user-directed*

## Context

WASAPI in shared mode ([0085](0085-a-sound-backend-declares-its-own-settings.md)) plays
through the Windows mixer, which costs at least one of its own periods and resamples to
the device's rate. Somebody with an audio interface has an ASIO driver for it: the
interface's buffers, filled directly, at the latency set in the driver's own control
panel. That is the Windows counterpart of JACK on Linux
([0190](0190-linux-sound-goes-through-jackd-when-one-is-running.md)).

## Decision

**ASIO is a second sound output in the Windows plugin, below WASAPI.** `AsioAudioOutput`
has priority 50 against WASAPI's 100, and is picked on the Sound tab
([0191](0191-the-sound-tab-picks-the-backend-where-more-than-one-can-play.md)). A sound
card's driver package installs an ASIO driver unasked, and a driver takes its card from
every other program while it plays, so being installed is not a wish to use it.

**Supported means a driver is registered** under `HKLM\SOFTWARE\ASIO`. The form asks which,
by the key it registered under; one since uninstalled stays chosen and says so.

**Hand-written, not NAudio.Asio.** A driver is called through its vtable, as libjack and
winmm are through P/Invoke, and nothing new is taken on. It is what lets the suite play
into `FakeAsioDriver`, a vtable laid out in memory, on any machine: the slots, the
structs, the callbacks and every sample type are checked in the gate, which has no
Windows and no driver.

**The driver lives on a thread of its own.** `IASIO` is a COM object in shape only, with
no proxy, so it is created, called and released on one single-threaded apartment,
`AsioThread`. The driver's audio thread calls back directly.

**Nothing is loaded until `Start`.** The rate is the engine's: a driver at another rate is
set to it, and one that cannot be, clocked from outside, refuses to start rather than play
at another pitch. The block size is the driver's preferred one. Flyback plays on the
first two outputs, or the left alone on a driver with one. Latency is the driver's output
latency.

**The driver writes its own format.** Little-endian 16, 24 and 32-bit integers, the 32-bit
containers of 16 to 24 bits, and 32 and 64-bit floats, each scaled and clipped.

**A reset request reloads the driver** with the same callback, on a pool thread: a driver
asks for one when its block size or rate is changed in its panel.

## Consequences

The callbacks carry no pointer back to the host, so one ASIO driver plays at a time, and a
second refuses to start while the first plays.

Which outputs play is not a setting; a patchbay in the driver's panel moves them. Line In
through ASIO's inputs would be a backend of its own, beside WASAPI's in `TODO.md`.

Only the fake driver is exercised by the suite; a real one was not run when this was
written.
