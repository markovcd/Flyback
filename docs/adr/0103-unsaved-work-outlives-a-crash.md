# ADR-0103: Unsaved work outlives a crash

**Status:** Accepted · 2026-09-19 · *user-directed* · builds on
[0072](0072-a-conversation-is-saved-with-the-patch-it-is-about.md) and
[0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md)

## Context

The only copy of unsaved work was the window's memory. The unsaved question on
closing guards against closing by mistake, and does nothing when the program
never gets to ask it: an exception nothing caught, a driver taking the process
down, the machine losing power. Everything since the last save was lost, and a
patch is often played with for an hour before anybody names it.

## Decision

**A window keeps a snapshot of its unsaved work, and the next start offers it
back.** Every two seconds the window asks the question the title's dot already
answers — is there anything to lose? — and, where there is and it has changed
since the last snapshot, writes the document to `recovery/` in the data folder:
the patch as JSON, the text where the text owns the patch (0068), the
conversation (0072), what an open bundle is carrying, the name, and the folder
its files are measured from. What is written is what the unsaved question would
have offered to save, so what comes back is the same document. A window with
nothing to lose keeps nothing, and the file is deleted as soon as a save makes
that true.

**On a timer, not on every edit.** A knob held and turned is an edit a frame;
two seconds is what a crash can cost. The write is off the UI thread and is
written beside the file and moved over it, so a crash mid-write leaves the last
whole snapshot. An exception reaching the dispatcher unhandled writes one more
synchronously before the program goes, since the UI thread is the one place the
document can still be read from.

**A crash is told from a running copy by a lock, not a process id.** Each window
holds a lock file of its own open and exclusive for as long as it runs. The
operating system lets go of it however the process ends, so a snapshot whose
lock can be taken belongs to nobody. A process id would have to be checked for
being reused, and a second copy of Flyback that is still running must never be
offered as a crash.

**The start offers the most recent orphan, once.** Restore puts it on the canvas
as the document it was, under its name and unsaved — the dot is on and the next
save asks where, because the file it was named after does not hold this. Discard
deletes it. Dismissing the question keeps it for the next start. A patch naming
a module no plugin now offers is refused as a file would be and kept. A second
orphan waits for the next start: a window holds one document. The question comes
after What's New and before a file the launch was asked to open, which then asks
about the restored work like any other.

**A window closed on purpose deletes its snapshot**, whatever was answered: the
question on closing has been asked by then, and Discard meant it.

## Consequences

A crash costs at most two seconds of work. A run with nothing unsaved writes
nothing but its lock. A bundle carrying large pictures or sounds is written with
them, which is a larger file each time it changes; it is written only when the document changes and never on the UI
thread. The snapshot is a file in the data folder and nobody's document: it is
not offered by the open dialog and is not meant to be found by hand.
