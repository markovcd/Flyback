# ADR-0111: The panel's actions are a row of glyphs

**Status:** Accepted · 2026-09-20 · *user-directed* · extends
[0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md)'s toolbar
to the inspector

## Context

The toolbar dropped its labels for glyphs a while ago, and every button on it
says what it does in a tip instead. The panel on the right never followed: the
buttons under a module's knobs — `Group 4 modules`, `Open 2 groups`,
`Delete module` — were words, stacked one to a line, each its own row down a
panel that is otherwise knobs and readings. A group's panel had four of them,
so a box's actions were a column of sentences under a list of sockets.

The captions also carried the counts, which is what kept them long. `Group`
counts because the graph refuses fewer than two; `Delete` counts because it
takes the whole selection and leaves sinks out of it; `Open` and `Close` count
the boxes the selection touches. None of that fits on a button without words.

## Decision

**Every action on the panel is a 34x30 glyph button, built by the toolbar's
own `Drawn` helper.** Same size, same padding, same named-and-tipped shape —
`Drawn(name, icon, tip)` was already there and needed nothing.

**They sit in one horizontal `ActionRow` under the name, above the
description**, rather than one to a line at the foot of the panel. A glyph is
the width of a button, so a column of them would leave the panel empty beside
it; at the top they are reached without reading past a module's description and
knobs or a box's sockets, which is what the panel is mostly made of. Grouping
comes first and deleting last, so the destructive button is at the far end of
the row.

**Where the row goes is settled before its buttons are decided.** Both panels
remember the index under the name — a module's after its category line, a
group's after `Group · 3 modules` — and insert the row there once they know
what is in it. A knob does not decide whether a module can be grouped, so the
two cannot be built in one pass.

**The counts move into the tips**, which is the only place a button without
words can say anything: "Delete these 3 modules  (Delete)", "Open the 2 boxes
the selection touches  (Ctrl+E)". The tip names the keyboard shortcut too,
the way the toolbar's do.

**Six glyphs join `Glyphs`**, drawn on the same sixteen-unit box and stroked
like the rest: `Group` (two modules inside a frame, corners rather than a
whole box, which at this size fills in), `Ungroup` (the same two adrift),
`OpenBox` and `ShutBox` (arrows apart and together), `Delete` (a bin) and
`Keep` (a bookmark).

**`KeepGroup` is handed the row, not the button.** The question it asks in
place — "Replace “Voice”?" — replaces the whole row at the row's own index,
so it stands where the buttons were and the panel does not move under the
hand that pressed one.

## Consequences

**A panel action is found by name now, not by caption.** `GroupInspectorTests`
and `SavedGroupTests` ask for `delete-group`, `keep-group`, `ungroup` and the
rest by `Name`, and read the count off the tip. It is the move
[0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md) already
made for `record`.

**The open button still turns round.** It is `open-group` with one glyph while
the box is shut and `close-group` with the other while it is open, so what a
test sees changes with the box, as the caption used to.

**A module's description starts one row further down**, and so do its knobs
and a box's sockets. The row is four buttons at its widest, so what it costs
the panel is one line.

**Two site screenshots were retaken**, `plasma-inspector.webp` and
`plasma-code.webp`, which both showed `Delete module` under a module's knobs.
