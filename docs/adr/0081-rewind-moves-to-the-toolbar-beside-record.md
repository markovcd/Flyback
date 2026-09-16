# ADR-0081: Rewind moves to the toolbar, beside Record

**Status:** Accepted · 2026-09-16 · *user-directed* · amends
[0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md), itself
amending [0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)'s
Output panel, itself [0037](0037-one-output-block-that-every-patch-has.md)'s

## Context

[0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md) moved
`Record…` to the toolbar and left `Rewind` as "the one standalone control"
under the Output panel's "Sound" heading — reachable only by selecting the
Output first, same as Record used to be, and for the same reason that was
worth fixing then: putting the patch back to zero seconds is a question
about the session, not about the module that happens to own the sockets it
reads.

With Record gone, the "Sound" heading in `BuildOutputSettings` introduced
nothing but Rewind. Emptying the panel of it rather than leaving a heading
over one lonely button is the same trade 0080 made when it deleted the
"Record" heading outright.

## Decision

**`rewindButton` is a toolbar button, in the transport group, before
`recordButton`.** The same group 0080 gave its own row between the patch
tools and the program group — rewind and record are both facts about the
performance, not edits `Ctrl+Z` takes back. Rewind sits first, the order a
tape deck's own transport reads: back to zero, then go.

**Its content is a glyph, `Glyphs.Rewind()` — a filled bar and a filled
triangle pointing at it, drawn on the same sixteen-unit box as the rest.**
Filled rather than stroked, like `Record`/`Stop` beside it and unlike every
other drawn icon: a bar-and-triangle outlined at this size reads as two thin
shapes, not the "skip to start" mark it is meant to be. `Filled` already
took a `Geometry`, not just the two primitives `Record` and `Stop` built —
`Geometry.Parse` supplies the two-subpath data directly, so `Filled` needed
no change.

**No keyboard shortcut.** Nothing was asked for one, and nothing on
`OnKeyDown`'s guard list — `Ctrl+Z`, `Ctrl+Y`, `Ctrl+L`, `Ctrl+R` — reads as
a natural mnemonic for it the way `R` reads for Record.

**The click handler is wired where the button is built, not in
`WireOutputControls`.** Record's handler lives there because `MarkRecordable`
— the state of the instrument, re-run on every recompile — has to decide the
button's tip and `IsEnabled` before any panel is ever shown. Rewind has
neither: it is always enabled and always says the same thing, so
`rewindButton.Click += (_, _) => { audio.Rewind(); preview.Rewind(); }` sits
beside `Marked(rewindButton, …)`, the same place `open` and `save` wire
their own handlers.

**`BuildOutputSettings` drops the "Sound" heading along with the button.**
The Output panel now carries only "Picture" — Size, Render, Processor.
Volume remains visible above as one of the Output's ordinary knob rows,
same as 0079 left it.

## Consequences

**Rewind is reachable with nothing selected**, same as Record — a session
that never opens the inspector can still put the patch back to zero.

**The Output panel has one heading now, not two**, and nothing under
"Sound" to point a heading at.

**`OutputSettingsTests`'s `ShowingSettings` tell moves from the `Rewind`
button to the resolution picker** — the one control left that only exists
while the panel is built into the inspector. `Rewind_is_on_the_output_panel`
is gone; a `rewind` toolbar-presence test and a filled-glyph test take its
place, mirroring the pair `record` already had.
