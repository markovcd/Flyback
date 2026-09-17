# ADR-0089: ffmpeg encodes what it can, and the AVI is the fallback

**Status:** Accepted · 2026-09-17 · *user-directed* · reverses
[0036](0036-export-video-as-motion-jpeg-in-an-avi.md)'s rejected fourth option
and demotes its decision to a fallback · reads
[0019](0019-no-third-party-dependencies-in-the-engine.md) as being about
dependencies rather than about programs · extends
[0082](0082-the-output-settings-move-to-the-settings-window.md)'s Recording
section

## Context

[0036](0036-export-video-as-motion-jpeg-in-an-avi.md) weighed four ways to get a
video file out and took the fourth, Motion JPEG in an AVI, writing both encoders
by hand. It rejected the third — shell out to ffmpeg — in one sentence: *on the
machines that have ffmpeg it is the least code, the smallest files and the best
compatibility; on the rest the feature is simply missing.*

That sentence is right about the risk and wrong about the remedy, because it
treated the two as exclusive. They are not. The AVI encoder exists and works, so
a machine without ffmpeg does not lose the feature — it keeps exactly what it has
today.

What 0036 predicted about the cost of its own choice has since been measured
rather than estimated, and it is worse than the estimate in both directions.
Ten seconds of Plasma at 960×540, the same patch through the same loop:

| Format | Size | Written in |
|---|---|---|
| `avi` Motion JPEG | 20.9 MB | 19.3 s |
| `mp4` H.264 | 0.85 MB | 4.5 s |
| `hevc` H.265 | 0.47 MB | 4.5 s |
| `webm` VP9 | 0.54 MB | 5.1 s |
| `prores` ProRes 422 HQ | 87.1 MB | 4.7 s |

Twenty-five times the size was the expected half of that. The unexpected half is
the time: writing the AVI takes **four times longer** than writing the H.264,
because `JpegWriter` is slower at compressing a frame badly than x264 is at
compressing it well. 0036's third option was rejected partly as the expensive one
and it is the cheap one.

AVI's 4 GB ceiling is not a limit somebody reaches by accident either. RIFF
counts in unsigned 32 bits, so `AviWriter` throws *"An AVI cannot exceed 4 GB"* —
at that rate, half an hour. A take is stopped by hand, so half an hour is a
length people record.

And Motion JPEG is not a format anything downstream wants. It is a fine input to
a transcoder, which is what 0036 said in its defence: *anything that wants an MP4
can transcode one*. The person doing that transcoding has ffmpeg. Asking them to
run it after the fact, over a file twenty-five times the size of the one they
wanted, is not a smaller ask than running it during.

The in-house sound is the same shape of problem one size down. A WAV is exact and
is the right thing to keep, but a take somebody is going to send to anybody is an
MP3, and the program had no way to write one.

## Decision

**A clip is written in one of nine formats, two of them here and seven by
ffmpeg.** `ClipFormats` is the whole list, one row each: the id a settings file
and the command line hold, the label the window shows, the extension, and the
ffmpeg arguments — or no arguments, which is what marks the two written here.

| | Picture | Sound |
|---|---|---|
| **Written here** | `avi` — Motion JPEG | `wav` — 16-bit PCM |
| **ffmpeg's** | `mp4` H.264, `hevc` H.265, `webm` VP9, `prores` ProRes 422 HQ | `mp3`, `m4a` AAC, `flac` |

**`IClipWriter` is what a clip is written through, and it takes frames as BGRA.**
`AviClipWriter` makes a JPEG of each one and `FfmpegClipWriter` hands them over
raw. Both the shell's take and `flyback-cli render` go through
`ClipWriter.Open`, so they cannot come to support different lists. Frames arrive
with a repeat count rather than in a loop, so a format that pays to compress one
pays once for a frame the source did not redraw in time.

**Raw frames rather than the JPEGs we already make.** Handing a real encoder
something to decompress first would pay for this program's compression and then
throw it away, and lose a generation doing it.

**The sound goes to a WAV beside the file and is muxed in at the end.** A process
reads one standard input and a clip has two streams, so the picture is piped
while the sound is written next to it, and a second pass copies the video through
untouched and encodes the audio. That is one rewrite of the file at the end and
nothing in the loop — the half that has to keep up with a performance. A clip
with no sound in it skips all of that and is one process writing the file
directly.

**ffmpeg is found rather than configured.** `Ffmpeg.OnPath` walks `PATH`; the
settings box is empty unless somebody has a particular ffmpeg in mind, and a path
that is not there falls back to `PATH` rather than failing. The Recording tab
says what was found — `ffmpeg version 7.1, on PATH at …` — and what the lack of
it costs. Looked for as the settings window opens rather than kept, because
ffmpeg can be installed, moved or removed between two openings.

**One Quality, 1 to 100, and each format reads it its own way**: a JPEG quality
to the AVI, and a constant rate factor to the rest over 14 to 40 — 85 → 18,
which is what a person means by "good", 50 → 27, 100 → 14. The sound formats fix
their own bitrates and ignore it, because nobody exporting a take is choosing
between a transparent MP3 and a smaller one.

**The extension decides, not the setting.** `flyback-cli render -o take.mp4`
needs no flag, and a name typed over what the record picker suggested means what
it says. `--format` overrides the extension, an id nothing defines being refused
rather than read as the extension's, and `--ffmpeg` names the executable. The one
extension two formats share is the exception: in the shell `.mp4` means whichever
of H.264 and H.265 the settings are on, since no name could otherwise ask for the
second.

**One take at a time, counting the one being finished.** Finishing runs behind
the window, so Record stays disabled until the last file is closed — a second
take could otherwise be written over it. The picker suggests the patch's name
rather than a fixed one, and closing the window waits for the file first.

**What a format asks of the container goes to the pass that writes the file.**
`+faststart` on the encode would be undone by the mux, which rewrites the
container, so `ClipFormat.Container` is kept apart from the video arguments and
handed to whichever pass is the last.

**A new settings file starts on H.264 wherever there is an ffmpeg to write it
with.** The one default in this program that is a question about the machine
rather than a number, and deliberately: an AVI always works and is not what
anybody wants when there is an alternative.

**A format already saved is never second-guessed.** Opening the program on a
machine with no ffmpeg shows H.265 still chosen, and starting a take then says
what is missing and how to fix it rather than quietly writing an AVI. A choice
rewritten to suit one launch would be lost the next time anything was saved.

## Consequences

**0019 still holds, read as it was written.** It is about *dependencies* — a
package that ships inside the engine and has to deploy everywhere it does. ffmpeg
is a program that may or may not be on a machine: nothing is linked, nothing is
deployed, and the sentence 0019 exists to protect is still true, because a
headless machine with no ffmpeg writes exactly the two formats it wrote before.
The snapshot tests still render through the hand-written PNG encoder, and
`AviWriter`, `JpegWriter` and `WavWriter` are all still here and still the
fallback rather than dead code.

**Stopping early still keeps what was rendered**, which 0036 got for free from an
index written at the end. It is not free here and it does hold: closing ffmpeg's
input makes it finalise its file, so a cancelled render is a shorter clip, and the
mux still runs.

**Finishing a take is no longer a header patch, so it left the UI thread.**
Muxing reads and rewrites everything recorded, which on a long take is seconds.
`LiveRecorder` closes the file as its worker's last act, `Stop` hands the shell
back at once and says *Finishing take.mp4…*, and the last word on a take — its
length, or what went wrong writing it — is reported when the file is actually
closed. The window is not frozen for the length of it.

**A failed mux keeps the picture.** The encoded video is moved into place under
the name that was asked for and the failure is reported, because a take that lost
its sound is still a take and is worth more than a tidy folder.

**An odd frame size is padded to the next even one.** H.264, H.265 and VP9 all
want 4:2:0, which halves both dimensions; every size the settings window offers is
even, but `--size 321x181` is not, so the arguments carry a pad filter and write
322×182. Padding rather than scaling — a black row costs nothing and resampling
the whole picture costs the thing being exported.

**Every format ffmpeg writes is a file rather than a stream.** `MovieRenderer`
keeps its `Stream` overload for the AVI, which is what the export tests write
into, and the path overload is the only way to reach the rest. ffmpeg is given
somewhere to write, not something to write into.

**Nine formats is a list, and a list invites a tenth.** Adding one is a row in
`ClipFormats` and nothing else — the settings pickers, `--format`, the record
dialog's file kinds and the extension lookup all read that list. MKV is the
obvious omission and is deliberate: `.mp4`, `.webm` and `.mov` cover who a clip
is for, and `-o take.mkv --format mp4` already works, ffmpeg taking the container
from the extension.

**The build image installs ffmpeg, for the tests rather than for the build.**
Every test that writes through ffmpeg skips itself where there is none — which
is the right behavior on somebody's laptop and the wrong one in the gate, since
the format most people will record in would then be the one thing CI never
exercises. Nothing links against it and nothing published carries it; it is a
line in the `apt-get` beside fontconfig.

**What ffmpeg says when it refuses is what the person is told.** Its standard
error is drained as it comes — a pipe nobody empties is what stops a process
complaining loudly enough — and the first few thousand characters of it come back
in the exception. There is no wrapping of ffmpeg's vocabulary into ours: an
argument it will not take is a sentence it wrote.
