# ADR-0229: A patch has one Song, shown in the Sequence pane

**Status:** Accepted · 2026-11-07 · *user-directed*

## Context

An Arrangement's parts are a grid in the inspector, and past a few dozen sections
the grid can no longer be read. The user wanted long sequences in a pane of their
own, a horizontal strip that can be opened and collapsed, of any length, and asked
that its parts loop at different lengths so a piece can drift. The Arrangement stays
as it is, for small sequences, as many as a patch wants.

A pane shows one thing. The text has to say which Song the pane is, and a pane that
switched between several would be a second inspector.

## Decision

**A patch has at most one Song (`seq.song`), and the pane under the canvas shows it.**

- **Its parts are named.** The name is what the text writes after the dot,
  `song.kick`, and what the socket says. A part is still wired by position, so a
  rename keeps its wires. A name is one the text can write, unique, and none of
  the Song's own outputs.
- **Each part loops at its own length**, up to 256 sections. `section` counts the
  longest part's, `progress` runs through each section, and `played` counts the
  sections played without coming round, which the pane's playheads read.
- **One a patch.** `Patch.CanAdd` refuses a second, adding one selects the one
  there, a paste leaves a second out, and a second `song()` in the text is
  `one-song`.
- **The text is a line a part**, the name and a colon first; the printer carries a
  long part on every 16 sections.
- **The pane** is drawn, not built of controls, and takes the inspector grid's
  gestures; it follows the playhead except while the pointer is over it or just
  after a scroll. Collapsed, it is one strip. It shows only while the patch has a
  Song.
- **The levels are reached by text** (`edit_patch`, `flyback-cli`), so an agent
  drives a Song without the pane.

## Consequences

- Section changes cost ops per distinct part length, as an Arrangement's do per
  section; 256 sections keeps one part within the op budget.
- `SongExtra` and `SongPart` are public, as every extra is.
