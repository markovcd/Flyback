# ADR-0024: Sample generation in the engine, the audio device in the shell

**Status:** Accepted · 2026-08-12 · amended by
[0025](0025-platform-io-behind-loadable-plugins.md), which moves the device out
of the shell and into a loadable plugin

## Context

There is no cross-platform audio output in the base class libraries. Making a
sound requires a third-party dependency, which runs straight into
[0019](0019-no-third-party-dependencies-in-the-engine.md).

But "make a sound" is two jobs. Turning a patch into a buffer of floats is
arithmetic; handing that buffer to a device is platform I/O. Only the second
needs anything outside the BCL — the same split
[0002](0002-split-engine-from-shell.md) already draws between the renderer and
Avalonia.

## Decision

`AudioRenderer` and `WavWriter` live in `Flyback.Core` with no dependencies.
`Flyback.App` owns the device behind one interface:

```csharp
public delegate void AudioCallback(Span<float> interleavedStereo);

public interface IAudioDevice : IDisposable
{
    int SampleRate { get; }
    bool IsRunning { get; }
    void Start(AudioCallback fill);
    void Stop();
}
```

`WasapiAudioDevice` implements it with NAudio 2.3.0 in WASAPI shared mode.

## Consequences

The engine stays dependency-free, so offline WAV export — the thing that has to
work headlessly — needs no device, no driver and no package. `WavWriter` is 60
lines of RIFF header and PCM, the direct counterpart to the hand-written
`PngWriter`.

Everything about audio *correctness* is testable without a sound card. Pitch,
stereo normalling, decimation, DC blocking, buffer continuity and the WAV
round-trip are all plain unit tests over `AudioRenderer`. Only the device itself
is untested, and it is the one class with nothing in it but glue.

NAudio was verified to resolve from plain `net10.0` before anything was built on
it — WASAPI output was the open question, since NAudio's Windows features often
sit behind a `net*-windows` target framework. It does not, so `Flyback.App` keeps
the portable TFM [0001](0001-target-net-10.md) chose, and the fallback of a
separate `net10.0-windows` project was not needed.

**This narrows the platform claim in [0015](0015-avalonia-for-the-ui-shell.md).**
Avalonia was chosen partly because it runs on Linux; `WasapiAudioDevice` does
not. The app still builds and runs everywhere — audio is opt-in and off by
default — but sound is Windows-only until a second `IAudioDevice` exists.
PortAudio, OpenAL and miniaudio bindings all fit the interface unchanged.

The callback never locks and never allocates. Everything it needs hangs off a
single immutable record swapped with `Volatile.Write`, so recompiling a patch
mid-buffer is a clean switch rather than a torn read, and register scratch is
sized on the UI thread by `AudioRenderer.Prepare`. This is the same discipline
[0018](0018-never-render-frames-on-the-ui-thread.md) established for video,
arrived at from the opposite direction: video must not block the UI thread,
audio must not be blocked *by* it.

While sound is playing it becomes the master clock and the preview follows it,
because audio cannot be stretched to catch up and video can drop a frame.
