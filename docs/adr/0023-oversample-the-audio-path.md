# ADR-0023: Oversample the audio path rather than band-limiting modules

**Status:** Accepted · 2026-08-12

## Context

The visual oscillators are naive: `Saw` is `fract(p) * 2 - 1`, `Square` is a
`step`. At video rates that is exactly right — a hard edge in an image is a hard
edge. At audio rates a discontinuity generates harmonics past the Nyquist
frequency, which fold back down as inharmonic tones. It sounds like buzzing, and
the folded partials move the wrong way when you change pitch.

The oscillators are not the only offenders, and this is the part that decides the
answer. `Fract`, `Floor`, `Step`, `Sign` and `Abs` are all discontinuous, and any
of them can sit anywhere in a patch. `Threshold` on a smooth signal aliases just
as badly as a saw.

Three options:

1. **Accept it.** Free, lo-fi, arguably on-brand for a video synth.
2. **Band-limited oscillator modules.** PolyBLEP variants of Saw and Square.
   Standard synth practice, cheap — and only fixes the three modules it touches.
   A patch aliasing through `Fract` still aliases, and the catalogue grows near
   duplicates of modules that already exist.
3. **Oversample the whole path.**

## Decision

Option 3. `AudioRenderer` evaluates the program at `SampleRate * Oversample`
(default 4×) and decimates through a 64-tap Blackman-windowed sinc lowpass,
cutoff at `0.45 / Oversample`.

## Consequences

Every source of aliasing is treated the same way, because the fix is downstream
of all of them. No module in the catalogue needs to know that audio exists — the
same `Saw` drives a screen and a speaker, which is the whole premise of
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md).

It is affordable precisely because the audio path is cheap. 4× on 48 kHz stereo
is 192k evaluations/sec against video's ~31M; the measured render is ~34× faster
than realtime including the filter.

**This reduces aliasing, it does not remove it.** The nonlinearity still folds
energy down; 4× oversampling pushes what survives far lower, but a sufficiently
harsh patch will still buzz. Stating this plainly matters more than the number.

The filter is the part most likely to break silently. Its state must persist
across buffers or every callback boundary clicks, which is why there is a test
rendering 2048 frames in one call and in two, asserting the results are
identical. It caught nothing on the way in; it exists because the failure mode is
a faint periodic tick that is easy to ship and hard to diagnose.

The aliasing test is a real measurement rather than a proxy: point-sampling a
30 kHz sine at 48 kHz aliases to 18 kHz, and the assertion is that oversampling
attenuates it below a quarter of the naive RMS. Setting `Oversample = 1` is what
that test compares against, so the option to disable it stays exercised.
