# ADR-0099: The computer keyboard can be laid out by scale, and the patch says how

**Status:** Accepted · 2026-09-18 · *user-directed* · extends
[0062](0062-indexed-polyphonic-midi-voices.md) and reuses the scale picker of
[0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)

## Context

The computer keyboard is read as a tracker's piano: white notes on the bottom
row, black ones above, and the same an octave up. That is right for somebody who
knows where the notes are and wrong for somebody who wants to play in a key
without knowing it: half the keys are wrong notes, and which half moves with
the key.

A Quantiser already solves the same problem for a signal. Its scale is a set of
pitch classes on the node, picked on a drawn octave.

The first version put the layout on each MIDI In, as a field and a scale. That
misled: there is one keyboard and it can only be laid out one way, so of two
modules asking for two scales one was obeyed and the other's panel showed a
setting that did nothing.

## Decision

**The layout is the patch's.** `Patch.KeyboardScale` is null for the piano and a
set of pitch classes for a scale. It is written into the file only when it is a
scale, so a patch that never asked for one saves as it always did. Empty is not
null: a scale with nothing picked plays nothing, which is what the panel shows.

**It is edited from any MIDI In on the keyboard.** Each shows the same
"computer keyboard" section — a Piano/Scale picker and the Quantiser's octave of
keys — headed as the whole patch's, so a change made on one is expected to show
on the others. A MIDI In on a hardware device shows none of it. Switching to
Scale starts on C major, as a fresh Quantiser does, or on the scale last
switched away from. Whenever the layout changes, including when a patch opens
on one, the status bar says what the keys now play, since nothing on the canvas
shows it.

**In `scale`, a row is an octave of the picked notes, side by side.** The home
row (`A` to `\`) is where the piano's lower octave was, the `Q` row an octave
up and the `Z` row an octave down. A row holds as many notes as are picked and
the keys after them play nothing, rather than running on into the next octave:
the key under a finger is then always the same degree of the scale. The `Z`
row has ten keys, so a scale of eleven or twelve loses its top notes there. A
layout that changes lets go of what is held, for the reason moving the octave
does.

**The text says it once, on a line of its own:** `keyboard scale [ C D E G A ]`,
or `keyboard piano`, which is what saying nothing means. A printing puts it
first. It is a statement rather than a module's argument because it is about no
module, and it is named rather than numbered so adding it renames nothing below
it. Saying it twice is refused. When the text is the document, a change in the
panel rewrites that line, adds it at the top, or takes it out for a piano. The
assistant sets it with `set_keyboard`, which takes no handle.

## Consequences

- Two MIDI Ins cannot play the keyboard in two different layouts. That was
  never possible; now nothing suggests it is.
- The file gained a field and the language a statement, neither of which an
  older build reads. An older build opens the patch as a piano, which is the one
  wrong answer that still plays.
