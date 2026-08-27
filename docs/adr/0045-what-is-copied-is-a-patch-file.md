# ADR-0045: What is copied is a patch file

**Status:** Accepted · 2026-08-19 · *user-directed* · uses
[0020](0020-json-patch-files-keyed-by-string-type-ids.md)'s format as a
clipboard format

## Context

Copy and paste needed a place to put what was copied and a shape to put it in.
Both had an obvious answer and a tempting one.

The tempting one is a field on the editor: a `Patch` held in memory, copied into
and pasted out of. It is a dozen lines, it needs no serialising, and it cannot
fail. It is also a copy that cannot leave the window — two Flybacks open side by
side could not pass a chain between them, which is most of what a person opens
two windows to do.

The shape had the same fork. A clipboard payload could be its own thing — a
fragment format, a list of modules and wires, whatever suited. But
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) already settled how a
patch is written down, and a selection of modules with the wires between them
*is* a patch. Inventing a second way to write the same thing would mean two
readers, two writers, and two answers to every question about missing plugins.

## Decision

**The system clipboard, holding the JSON a patch is saved as.** `Copy` builds a
`Patch` of the selected modules and writes it with `PatchIo.ToJson`; `Paste`
reads it with `PatchIo.Read`. No new format, no new reader, and nothing in
`Flyback.Core` learns that a clipboard exists.

**Which makes three things true for free.** A chain copied in one window pastes
into another. What lands in a text editor is a readable patch file. And pasting
a whole saved `.fbk` into the canvas merges it — which is a thing worth being
able to do, and it fell out rather than being built.

**Only wires with both ends inside the selection come.** A wire with one end
outside has nothing to plug into when it arrives, and guessing at what it should
reach for instead would be inventing a patch nobody drew.

**The Output never travels.** A patch holds exactly one
([0037](0037-one-output-block-that-every-patch-has.md)), so it is not a thing
that can be pasted. Copy leaves it out, and paste drops any it finds — which it
always will, because `PatchIo.Read` adds one to anything short of it, and
because a whole saved patch has one of its own.

**Every pasted module gets a fresh id and the wires are rewritten to match.**
The ids in a fragment are the ones it was copied from, so without this a paste
into the patch it came from would name the modules already there rather than
adding any.

**Amended: a box travels with what is in it, and only whole.** A group is a
drawing and not a definition, and the `Groups` list is already part of the file
— so a fragment carries one at no cost and no new format. `Copy` takes a group
every one of whose members is coming; `Paste` draws it again round the modules
that arrived, with a fresh id of its own for the same reason the modules get
fresh ids. A group with a member left behind is dropped rather than clipped:
what arrived would be a different box from the one on the canvas — a different
shape, with a different set of sockets — which is the objection the
half-selected wire gets, in the other direction. The sockets on the edge name a
module and a port rather than a number of the box's own, so pasting rewrites
them to the ids they now point at, and the ones with nothing wired to them come
too — the edge of a box is arranged rather than derived, and a paste that
recomputed it from the wires would hand back a box somebody had already tidied.

**The stamp is what checks the plugins.** `ToJson` records which providers the
modules came from and `Read` reports the ones this build has not got, so pasting
a fragment that needs a missing plugin is refused with the sentence
`PatchLoad.Summary` already words — the same sentence opening such a file gives.
Nothing about that check is written twice.

**Where it lands is the editor's, not the engine's.** `PatchClipboard.Paste`
translates by whatever it is handed. Working out a sensible place needs the size
of a drawn node and the bounds of a viewport, and neither is the engine's
business — the same split [0044](0044-lay-patches-out-in-layers-not-with-springs.md)
makes for the layout, through the same reasoning.

**The keys are the canvas's, not the window's.** Undo and redo are handled on the
window ([0039](0039-one-window-class-across-a-file-per-region.md)) because an
edit is as likely to have been made in the inspector as on the canvas. Copy is
the opposite: Ctrl+C in a text box means the text in it, and a window-wide
handler would have to work out which of the two was meant. Handling them on the
canvas answers that by not asking — they only fire while the canvas has the
focus.

## Consequences

**A paste can fail, and failing is a sentence rather than an exception.** The
clipboard holds whatever anybody last put there, so the ordinary way to reach a
failed paste is having copied something else entirely. That gets "the clipboard
does not hold a patch"; a fragment from a build with plugins this one lacks gets
the missing-plugin sentence; and neither changes the patch. An in-memory buffer
would have had none of these cases and none of this reach.

**Copy is asynchronous, so the gesture is.** `IClipboard` is async on every
platform Avalonia supports, and a key press has nobody to hand a task back to. So
the handler is `async void` with a catch that cannot be skipped — an exception
escaping it would have no caller to reach.

**A cut that cannot copy deletes nothing.** Otherwise it would be a delete
wearing a cut's name: the modules would be gone and the clipboard would still
hold whatever it held before.

**Paste lands in the middle of the view, then steps clear.** The middle is where
`AddNode` puts things and for the same reason — where a fragment was copied from
may be a screen away. But the middle of the view is also where the patch is, so
landing there means landing on top of something, which reads as nothing having
happened. It steps down and right until it is clear with room to spare, capped:
a dense enough patch has no clear middle, and walking off the edge looking for
one would be worse than overlapping. What arrives is selected, so dragging it
somewhere better is one gesture.

**Nothing keeps the clipboard format and the file format from drifting, because
they are the same format.** That is the point, and it is also the whole of the
risk being taken: a change to how a patch is written is a change to what an old
clipboard payload means. The version stamp already answers that for files
([0020](0020-json-patch-files-keyed-by-string-type-ids.md)), and it answers it
here unchanged.
