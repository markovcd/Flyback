# ADR-0073: An Analyzer is a Scope filled with a spectrum

**Status:** Accepted · 2026-09-15 · *user-directed* · extends
[0053](0053-a-scope-records-what-the-speakers-played.md) with a second way of
filling its buffer

## Context

A Scope shows what the speakers played as a waveform, and a waveform is the wrong
picture for most questions about a sound. Whether a filter is closing, where a
drone's harmonics sit, whether the bass is masking the kick — these are questions
about frequency, and a chart of the last fiftieth of a second answers none of
them. The patch had every piece a spectrum analyzer needs except the transform:
a ring of what was played, refilled into a buffer once a frame, drawn by a module
that reads that buffer as a table.

[0058](0058-the-picture-is-told-how-loud-the-sound-is.md) ended by naming band
energy as the missing half of the Meter. That is a different module — numbers
played into the picture, which keeps the shader — and this is not it. This is the
chart.

## Decision

**The Analyzer is a Scope in every respect but what fills its buffer.** It taps its
input, never lowers that input into the picture, and draws from a table refilled
by `Traces.Refresh`. `NodeDef.ChartsSpectrum` is the one new flag: it rides on
`ChartsSignal`, reaches the refill as `TapSpec.Spectrum`, and switches the fill
from `DelayState.CopyTrace` to `Spectra.Chart`. Nothing about the tap, the ring,
the pairing by node id or the shell's selection changes.

**The transform is an averaged periodogram with a fixed segment.** Hann-windowed
segments of 16,384 evaluations — about 85 ms, a bin every 12 Hz at the oversampled
rate — half-overlapping, their power averaged. The window knob chooses how much of
the past is averaged, not how fine the bins are: a longer window asks for a
steadier reading, and a single transform thirty seconds long would be six million
points. At most eight segments are taken, spread evenly across a long window, so
what a frame costs does not grow with the knob.

**The buffer holds linear amplitude on a log-frequency axis, and the decibels are
the module's.** The refill lays 20 Hz to 20 kHz across the buffer's points,
taking the loudest bin in each for the reason a Scope's bucket takes its peak.
The module takes the log. That keeps 'range' and 'scale' as sockets that can be
swept without the refill knowing, and it keeps silence meaning silence at the
buffer's ends, where the table read returns nought — as decibels, nought would be
the top of the chart.

**The chart is the Scope's chart, standing up from the bottom.** `Charted` gained
a ground for the fill, so the same trace, grid and overload bar serve a spectrum.
The grid's columns are ruled on the decades rather than evenly from the edge, and
a square is an eighth of 'range' in height.

## Consequences

**It has the Scope's three cliffs,** and says so in its description: nothing while
sound is off, nothing the Output does not reach, and only the past.

**It gives up the shader,** as a Scope does, because its program reads a table.

**The lowest octave is coarse.** Two bins cover 20–40 Hz. That is the price of a
reading that updates at frame rate, and it is stated in the description rather
than hidden by interpolation that would look finer than it is.

**A tone between two bins reads up to about a decibel and a half low.** That is
the Hann window's scalloping. A flat-top window would read amplitude exactly and
blur neighboring tones together; for a picture of where the energy is, resolution
is the better half of that trade.

**It costs a frame at most eight transforms of sixteen thousand points**, on the
drawing thread, and the same twenty-three megabyte ring a Scope does on the audio
side.
