# ADR-0099: The computer keyboard can be laid out by scale, and the patch says how

**Status:** Accepted · 2026-09-18 · *user-directed* · extends
[0062](0062-indexed-polyphonic-midi-voices.md) and plays the Auto Chord's scales

## Context

The computer keyboard is read as a tracker's piano: white notes on the bottom
row, black ones above, and the same an octave up. That is right for somebody who
knows where the notes are and wrong for somebody who wants to play in a key
without knowing it: half the keys are wrong notes, and which half moves with
the key.

An Auto Chord already names the scales worth playing in: the modes of the
major, harmonic minor and melodic minor scales and five more, each seven notes
on a tonic.

The first version put the layout on each MIDI In, as a field and a scale. That
misled: there is one keyboard and it can only be laid out one way, so of two
modules asking for two scales one was obeyed and the other's panel showed a
setting that did nothing.

## Decision

**The layout is the patch's.** `Patch.Keyboard` is null for the piano and a
tonic and one of the Auto Chord's scales otherwise. It is written into the file
only when it is a scale, so a patch that never asked for one saves as it always
did.

**It is edited from any MIDI In on the keyboard.** Each shows the same
"computer keyboard" section — a Piano/Scale picker, then a tonic and the Auto
Chord's list of scales — headed as the whole patch's, so a change made on one
is expected to show on the others. A MIDI In on a hardware device shows none of it. Switching to
Scale starts on C major, as a fresh Auto Chord does, or on the scale last
switched away from. Whenever the layout changes, including when a patch opens
on one, the status bar says what the keys now play, since nothing on the canvas
shows it.

**In `scale`, a row is an octave of the scale from its tonic, side by side.**
The home row starts on the tonic in the octave the piano's lower octave was,
the `Q` row an octave up and the `Z` row an octave down. The seven notes take
`A` to `J` and the keys after them play nothing, rather than running on into
the next octave: the key under a finger is then always the same degree of the
scale. A layout that changes lets go of what is held, for the reason moving
the octave does.

**The text says it once, on a line of its own:** `keyboard scale [ D dorian ]`,
a tonic and a scale by id, or `keyboard piano`, which is what saying nothing
means. A printing puts it first. It is a statement rather than a module's argument because it is about no
module, and it is named rather than numbered so adding it renames nothing below
it. Saying it twice is refused. When the text is the document, a change in the
panel rewrites that line, adds it at the top, or takes it out for a piano. The
assistant sets it with `set_keyboard`, a tonic and a scale, which takes no
handle.

## Consequences

- Two MIDI Ins cannot play the keyboard in two different layouts. That was
  never possible; now nothing suggests it is.
- The file gained a field and the language a statement, neither of which an
  older build reads. An older build opens the patch as a piano, which is the one
  wrong answer that still plays.
- A scale is one of the Auto Chord's, never an arbitrary set of notes. A
  pentatonic or a scale of twelve is a Quantiser's job.

## Amendment, 2026-09-21: a default for a patch's first MIDI In

Settings → MIDI has a **New keyboard** choice, Piano or Scale, kept with the
other output settings and Piano until changed. It is read once: when a MIDI In
is added from the palette to a patch that has no MIDI In and no scale, and
Scale is the default, the patch takes C major in the same edit as the module.
A patch that already has a MIDI In or a scale, one that is opened rather than
added to, and one whose text is the document are never touched.
