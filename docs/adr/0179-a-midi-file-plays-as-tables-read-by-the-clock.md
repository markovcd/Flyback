# ADR-0179: A MIDI file plays as tables read by the clock

**Status:** Accepted · 2026-10-06 · *user-directed* · follows
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md), which let a
patch name a file, and [0062](0062-indexed-polyphonic-midi-voices.md), whose voices
it mirrors

## Context

A patch could be played by a keyboard but not by a score. A MIDI In reads live
values the shell writes as hands move (0056), so a file played through the hub would
be live-only: an export, `flyback-cli render`, the viewer and a seek would all hear
nothing, and an agent could not check it without a window.

## Decision

**A MIDI File module names a `.mid` file and reads it at a position.** Its `in` is
seconds into the file, Time without a wire, as a Sample's is (0052). Its outputs are
a MIDI In's, `pitch`, `gate`, `velocity` and `trigger`, and `length` in seconds.
Rate, looping and scrubbing are wired, not set.

**A voice is four tables.** `LoadedMidi` holds the file's notes in seconds, every
tempo change applied; `MidiLine` plays one voice of it as four `LoadedSample`s at
1,000 entries a second, read by `OpCode.Table`. Nothing in the program counts or
remembers, so the speakers, the picture, an export and a seek hear one file, and
the GPU reads the same tables (0167). The gate is written to drop for one entry as
each note lands, so a legato run retriggers an envelope as a MIDI In's does, and the
trigger is a five-entry pulse. A read interpolates over a millisecond, which is the
price of no click and the grain of the timing.

**A voice is picked as 0062's are.** `voice` 1 to 8 is that one of the notes held at
once, the first free slot taking each; a ninth goes over voice 1 and the note under
falls back when it ends. `voice` 0, the default, is one voice playing the newest key
held. `channel` 0 hears every channel, 1 to 16 one. Several modules on one file play
a chord.

**The path is `MidiFileExtra`, the voice and channel `MidiLineExtra`.** Two extras,
because a declared field cannot share an object with a bare path without the editor
overwriting it. `MidiFileExtra` folds both onto the context, so a module is handed
the one voice it plays. Sample's machinery carries the rest: relative to the patch,
bundled and rebased by `Files` and `Rebase`, written as `file("x.mid", voice: 2)`,
set by the assistant's `set_sample`.

**`ISampleLibrary` gains `FindMidi` and `ExplainMidi` as default members**, so no
implementer breaks (0102). `SampleLibrary` and `BundleFiles` answer; the web page's
library does not yet, and a MIDI File there says nothing can open one.

**The reader takes no package and trusts nothing** (0019). `MidiFileReader` reads
format 0, 1 and 2 with every track over one tempo map, running status and SMPTE
timing. A length is held to what the bytes carry, a truncated track gives what it
had, and a file over 8 MB, 200,000 notes or twenty minutes is refused by name, since
each voice costs four tables of that many thousand floats.

## Consequences

A voice of a ten-minute file is about 10 MB of tables. A file is read once and its
voices built once per (voice, channel), cached, so an edit recompiles against both.

Timing is a millisecond, and a note shorter than that still lasts one entry.
Program changes, controllers, pitch bend and sustain are not read. Tempo changes
are, and a tempo wired through `in` is how a patch speeds a file up.

The web editor and viewer do not play a MIDI File until their library is taught
the file; the engine side is shared.
