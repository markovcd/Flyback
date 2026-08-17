# ADR-0036: Export video as Motion JPEG in an AVI

**Status:** Accepted · 2026-08-17 · *user-directed* · extends
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md), constrained by
[0019](0019-no-third-party-dependencies-in-the-engine.md)

## Context

Both sinks could be written to a file and neither could be written to the same
file. `Save frame…` wrote one PNG and **Render audio…** wrote ten seconds of WAV,
so the instrument could show you a moment and play you a minute, and could not
hand you the thing it actually makes: a picture that moves, with its own sound
under it.

The length was the other half of the gap. Ten seconds was a `const` in
`MainWindow`, which is defensible for a format nobody keeps and wrong for one
they do — a patch is an endless function of `(x, y, t)` and where to stop is the
one parameter of an export that cannot be defaulted from anything.

The hard part is the container, because [0019](0019-no-third-party-dependencies-in-the-engine.md)
rules out reaching for a codec. Four ways to get a video file out:

1. **Uncompressed AVI.** About 150 lines and lossless. Also 1.8 GB for ten
   seconds at 1080p, which reaches AVI's 4 GB ceiling in twenty — a format whose
   own limit is twenty seconds is not an export.
2. **MP4 with H.264.** The file everybody wants. Nobody writes an H.264 encoder
   as part of a synthesiser, and pulling one in ends 0019.
3. **Shell out to ffmpeg.** Least code, smallest files, best compatibility — on
   the machines that have ffmpeg. On the rest the feature is simply missing, and
   "export works here and not there" is the property the plugin boundary
   ([0025](0025-platform-io-behind-loadable-plugins.md)) exists to keep out of
   the engine.
4. **Motion JPEG in an AVI.**

## Decision

Option 4. **Every frame is an independent JPEG; the AVI interleaves those with
16-bit PCM.** Both encoders are written here, beside the two that already were.

- `JpegWriter` — baseline, 4:2:0, the Annex K tables, no restart markers. An
  instance rather than a static class, unlike `PngWriter` and `WavWriter`,
  because a movie is thousands of calls and the colour planes are worth keeping
  between them.
- `AviWriter` — RIFF, one video stream and optionally one audio stream, `idx1`
  at the end.
- `MovieRenderer` — drives both sinks into one file, and is the only place in
  the program where the two are advanced against a shared clock.

MJPEG is the one compression that fits in a container this simple, because it
needs no notion of a frame that depends on another one. Measured against
ffmpeg's own MJPEG encoder on the same frame at a comparable size: 41.6 dB
against 43.2 dB, with the luma plane slightly *ahead* and the chroma behind,
which is the cost of box-averaging the chroma down rather than filtering it.

The length is a control on the toolbar, and both exports read it.

## Consequences

An export is the first thing in this program that takes long enough to need
saying so. `MovieRenderer` reports progress per frame and stops on request, and
stopping **keeps what was rendered** — the AVI is closed properly and is a
shorter video rather than a broken one. That falls out of the format rather than
being designed: an index written at the end can only describe chunks that exist.

The video sink is rendered on the CPU even when the preview is on the GPU. Under
[0035](0035-a-glsl-backend-for-the-video-path.md) those two are allowed to differ
in their last bits, and an export should be the one that is reproducible.

Feedback works here, and this is the first export where it could. One
`SynthRenderer` runs the whole clip, so each frame reads the one before it
exactly as on screen — `Save frame…` renders into a fresh renderer and therefore
could never show what `Feedback` does. The audio side keeps one `AudioRenderer`
for the same reason: an oscillator's accumulated phase
([0030](0030-oscillators-accumulate-their-phase.md)) and a delay line's tail
([0027](0027-delay-lines-give-the-audio-path-a-memory.md)) run the length of the
export or they are not those things at all.

Time comes from the frame number and not from an accumulated delta, and the audio
cursor counts samples. That is what keeps the two streams ending together at a
rate whose samples-per-frame is not a whole number.

What this costs is the file. MJPEG is an old compression and every frame pays
full price: 960×540 at quality 85 measures 1.6 MB a second on Drone and 2.6 on
Nebula, where H.264 would be a tenth of that. It is also 4 GB per file, which at
those rates is half an hour or so. Both are stated in the writer and neither is
worth ending 0019 over — anything that wants an MP4 can transcode one, and the
file this writes is the one every tool will accept as input.

A patch with nothing wired into the Output's `left` or `right` gets a video-only
file rather than a silent track, because a stream claiming to carry sound that
carries none is a worse answer than no stream. That test was originally "is there
an Audio Output module", which
[0037](0037-one-output-block-that-every-patch-has.md) turned into a question
about sockets rather than about modules — the sink is always there now, so its
presence says nothing about whether anything reaches it.
