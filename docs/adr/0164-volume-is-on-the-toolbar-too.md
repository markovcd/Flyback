# ADR-0164: Volume is on the toolbar too

**Status:** Accepted · 2026-09-29 · *user-directed* · builds on
[0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md) and
[0081](0081-rewind-moves-to-the-toolbar-beside-record.md)

## Context

Volume is a socket on the Output ([0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md)),
so turning it meant finding the Output on the canvas and selecting it first. How loud
the speakers are is looked for on the toolbar, beside the rest of the transport.

Taking the socket off the Output would lose what it does as a socket: about forty presets
set it, a wire into it fades a take out, and `out.volume` is written in the text. A
second, listener's volume beside it would bring back the two controls for one question
that [0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md) removed.

## Decision

**The toolbar carries a slider on the Output's Volume**, after the seek bar in the
transport group, behind a speaker glyph. It is the same socket, not a copy: turning it is
the edit the Output's knob row makes, under the same undo key, so a drag is one step to
take back and the text is written once the hand comes off. It follows the patch through
every edit, undo and opened patch.

**It is grayed out while something else drives Volume**, a wire or a panel knob, and its
tip says which. The Output's knob row stays where it was.

## Consequences

Volume is an edit that sits among controls that are not; the toolbar's comment says so.
The web editor carries the slider too, since it edits the patch and needs no sound.
