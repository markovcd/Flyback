# ADR-0139: A patch keeps to an instrument's clock

**Status:** Accepted · 2026-09-23 · *user-directed* · extends
[0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md) and
[0062](0062-indexed-polyphonic-midi-voices.md)

## Context

Everything a patch does in time is a function of the clock. A Tempo is
`bpm / 60 × t`, a sequencer steps on the beats that arrive at its input, and
nothing outside the program can move `t`. Next to a drum machine or a hardware
sequencer — an Elektron Syntakt was the case in hand — that is the one thing
wrong with it: the box has a tempo, a Start button and a bar line, and the patch
has its own idea of 120 that drifts off the box's downbeat within a bar. A MIDI
cable carries exactly what is missing, twenty-four ticks a beat and four
transport messages, and the contract dropped every one of them at
`MidiMessages.Of` before the hub ever saw it.

The block a program reads its live inputs from is single floats, written on the
driver's thread and read without a lock on the thread that plays
([0086](0086-panel-knobs-are-read-as-live-values.md)). That shape decides how a
clock can be handed over. A beat is a count of ticks and moves in steps; the ear
reads 192,000 evaluations a second and would hear the stairs. A line through the
beat, `offset + rate × (t − at)`, is smooth, but it is three numbers that have to
move together, and three floats cannot: an evaluation that reads a new `offset`
beside an old `at` is a twenty-fourth of a beat off for one sample, which in a
sequencer's input is a wrong step and a click. The hub cannot timestamp a tick
in the program's time either, since the speakers' clock advances a buffer at a
time and the picture's a frame.

## Decision

**The contract hears the clock and the transport.** `MidiAction` gains `Tick`,
`Start`, `Continue`, `Stop` and `Position`; the last carries the song position
in sixteenths. Active sensing, the wheel and the rest still come back null.

**A Clock In is a module, one per instrument.** It follows the instrument its
`follows` field names — filed under the same `midi` key as a MIDI In's
`device`, so the two name an instrument the same way in a file and in the text —
and has `beats`, `bpm`, `running` and `reset` outputs. A fresh one follows the
first instrument plugged in, because the computer's keyboard keeps no clock.

**The shell publishes a beat and a rate, and the program draws the line.**
`MidiClock` in the engine counts ticks into a beat, measures the tempo from
their spacing on whatever steady clock the shell keeps, and writes five values
per instrument: the beat as of the latest tick, the rate (nought while stopped),
the tempo, whether it is running, and a count of Starts. The module keeps the
moment the beat last changed in a cell and runs `beat + rate × (in − at)` from
there, against the program's own clock — the one timebase every evaluation
already shares, and the one that puts a tick's arrival on the exact sample it
was seen. No two of the published numbers have to agree with each other, so a
torn read costs at most a rate change over one tick's width.

**Jitter is absorbed, the transport is not.** A tick lands early or late by the
cable and by wherever the speakers' buffer happened to be, so the line jumps a
little each time it is re-anchored, and again when the tempo moves or a Stop
takes the rate away. The difference between where the line as it was said the
beat is and where the line as it is now says goes into a slip cell that is let
go over a quarter of a second, which keeps `beats` continuous and moves it never
backwards. A jump of half a beat or more is the transport, and so is a Start,
and both go straight through. A Continue re-anchors the line at the moment the
rate comes back, so the beats go on from where they held rather than from where
they would have got to.

**A tick asks for nothing.** Forty-eight a second at a dance tempo are written
into the block and no more: the picture is redrawn anyway while the clock runs,
and a tick is not somebody playing, so it is not counted as an instrument in
use. A button on the transport is both.

## Consequences

A patch keeps to a Syntakt's bar by wiring `beats` into a sequencer's `in` and
setting the sequencer's rate to steps per beat, and every Tempo, Euclid and LFO
downstream of it keeps to the same bar in the picture and the sound at once.

A Clock In costs five cells, which on the video path are five planes.

A tick off the line is eased over a quarter of a second rather than jumped, so a
sequencer driven from `beats` is at most a few milliseconds off the box between
ticks and never skips a step. A Stop lands up to a tick after the tick it
follows, and the beats ease back by that much.

The anchor is a float on the GPU, so a Clock In that has run for a day is about
eight milliseconds coarse there; the processor keeps it in doubles.

Sound from the box is not this decision. Overbridge is not open, so a patch that
draws what the box plays needs an audio input contract across the three platform
plugins, which is a piece of work of its own. Sending the box a clock or notes
is not either: the box is the better sequencer.
