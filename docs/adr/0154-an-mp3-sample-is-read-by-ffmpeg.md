# ADR-0154: An MP3 sample is read by ffmpeg

**Status:** Accepted · 2026-09-28 · *user-directed* · extends
[0089](0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md) from
writing to reading · leaves [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)
as it is

## Context

A Sample played WAVs only, read by a hand-written decoder. People's sound files
are mostly MP3s. [0019](0019-no-third-party-dependencies-in-the-engine.md) rules
out a decoder package, and a Layer III decoder written here is a thousand lines
of Huffman tables and filter banks to keep. ffmpeg is already found, picked in
Settings → Recording and trusted to write takes.

## Decision

**ffmpeg decodes an MP3; the WAV reader stays.** `SoundReader` tells the two
apart by their first bytes and hands an MP3 to `Mp3Reader`, which runs ffmpeg
from a file to a temporary 32-bit float WAV and reads that back with
`WavReader`. The mixdown to mono and the ten-minute cap stay in one place.

ffmpeg is given a file, never a pipe: only from a file does it trim the padding
an encoder adds, so an MP3 lasts as long as the sound it was made from. A
bundle's MP3 is written to a temporary file first.

The ffmpeg is the one picked in the settings, else the one on `PATH`. The CLI
and the viewer have no setting and use `PATH`. A bundle carries an MP3 as it
carries a WAV.

## Consequences

A machine without ffmpeg still plays every WAV, and a Sample pointed at an MP3
there says what it needs rather than that the file is not a sound. A bundle
with an MP3 in it plays silent on such a machine.

Reading an MP3 starts a process and writes a temporary file, once per file:
`SampleLibrary` keeps what it read, and a new ffmpeg picked in the settings
clears what it had refused.
