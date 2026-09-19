---
name: site-audio-tracks
description: Use when a Flyback preset that has a track on the website changes audibly, or when adding a listening-row MP3 to site/ - how the MP3s and their data-peaks bars are rendered and encoded, and how to check a preset's spectrum while tuning.
---

# The website's audio tracks

The two tracks in `site/assets/audio` (acid, slow-weather) are the first minute of the preset rendered with `flyback-cli render patch.fbk --out x.wav --seconds 60` (48 kHz), then ffmpeg two-pass `loudnorm` to -16 LUFS (TP -1.5, LRA 11, linear), `afade=t=out:st=56:d=4`, 44.1 kHz, `libmp3lame -q:a 2`. Both measure -16.0/-16.1 LUFS integrated, so a new one must match. The site lists the tracks as rendered by the CLI with nothing added; a track at a different loudness or with different bars would stand out from the other.

The `data-peaks` attribute on the site's `.player` is 96 values: the RMS of each of 96 equal slices of the decoded MP3, divided by the largest, floored at .10, written like `.61` with no leading zero. It is a dozen lines of Python to rewrite.

Whole band has no MP3. A whole song goes under "Whole tracks, seen and heard" as a YouTube clip: the user records and uploads it, and the site embeds `youtube-nocookie.com/embed/<id>` with no `si` parameter.

## When a preset with a track changes audibly

Re-render, re-encode, replace the peaks and check the blurb in `site/index.html`, all in the same commit.

## Tuning by spectrum

An octave-band long-term spectrum of the WAV (numpy, 8192-point Hann windows, bands 20-40 up to 10k-20k, dB relative to the loudest band) is what showed Slow weather was too dark. The user wants an even spectrogram with the top never dominating; a tilt of about -3 dB an octave through the middle was accepted.

Render timing on this machine is noisy (Bronze went from 0.62x to 2.1x real time between sessions), so compare a new preset against a shipped one in the same run rather than against a remembered number. See `authoring-presets`.
