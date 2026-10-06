# ADR-0178: A Line In hears the microphone while the sound plays

**Status:** Accepted · 2026-10-06 · *user-directed* · follows
[0025](0025-platform-io-behind-loadable-plugins.md), which put sound out behind
plugins, and [0024](0024-audio-device-in-the-shell.md); implemented in
`Flyback.Ui/Audio/LineIn` and `src/plugins/Flyback.Plugins.LinuxIO`

## Context

A patch could only make sound. Playing a voice or an instrument through its own
echo, filter or vocoder needs the microphone as a source.

The sound program is evaluated a sample at a time at the oversampled rate, and
every backend and the interpreter agree on it (0035). A source that arrives from
outside has to reach it without a new op in each of them.

## Decision

**A Line In reads two live inputs, `line-in/left` and `line-in/right`.** They are
the same `LoadLive` a played key is, so no op, no IL, JavaScript or shader change.
`AudioRenderer` writes them once per frame from an `ILineInSource`, so the
oversampled evaluations inside one frame hear one instant. With no source they are
zero.

**A plugin offers a sound input the way it offers a sound output.** `IAudioInput`
lists itself and declares its form; `Create` makes an `IAudioCapture` that opens
nothing until it is started with a callback. `IPluginRegistry.AddAudioInput` is
the registry's new method, so the contract only grows (0102). The device chosen
is filed under the backend's id in the settings, apart from the sound output's.

**The microphone is open only while the playing sound reads it.** `LineIn` asks the
running program's live inputs after every recompile and every start or stop, and
opens the capture only if the device is running and the program names a Line In
that reaches the Output. Pausing, stopping or taking the module out lets it go.
A device that will not open, or stops by itself, is said once and left until the
settings are saved.

**What it hears crosses threads in a ring.** `LineInFeed` has one writer and one
reader, no lock, and the reader skips ahead rather than let the lag grow past
about 85 ms. An empty ring is silence.

**A render hears a file.** An offline render has no microphone, so
`flyback-cli render --input <file>` plays the file from its start as a
`RecordedLineIn`, resampled to the engine's rate; without it the Line In is
silent. It is what makes the module checkable with no hardware.

## Consequences

Hearing and playing run on two clocks, so the feed drops or repeats a few frames
over a long run and adds the capture buffer to the latency. Feedback from the
speakers is the person's to avoid, and the module's help says so.

Only Linux has a backend (ALSA, through the plugin that already plays and routes
MIDI). On Windows and macOS a Line In is silent and the window says no input is
installed; their backends are in `TODO.md`. macOS will also need the microphone's
usage string and entitlement in the app bundle, which no part of the build has.
