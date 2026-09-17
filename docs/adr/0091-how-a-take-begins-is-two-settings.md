# ADR-0091: How a take begins is two settings

**Status:** Accepted · 2026-09-17 · *user-directed* · amends
[0090](0090-a-take-is-counted-in-and-starts-at-zero.md)

## Context

[0090](0090-a-take-is-counted-in-and-starts-at-zero.md) fixed the count-in at
three seconds and made the rewind unconditional, on the grounds that a
count-in is a convention rather than a preference. Both are now asked for as
settings, which is the answer to the question that record left open: three
seconds is the right count for standing ready with an instrument, and the
wrong one for catching something the patch is about to do on its own, and a
rewind is exactly wrong for recording a session as it stands — an envelope
halfway down, a loop full of what came before, a sequencer eighty bars in.

## Decision

**`CountInSeconds` and `RewindBeforeTake` are `OutputSettings`, in the
Recording section**, kept in `output.json` with the rest and read by
`CountInAsync` rather than compiled into it. Neither reaches the engine or a
patch file: how a take begins is a property of the machine, the same as the
frame rate beside it (ADR-0037).

**The count is a picker of `None, 1 s, 2 s, 3 s, 5 s, 10 s`, defaulting to
three.** A list rather than a number box, like every other duration in this
window: nobody wants a count-in of seven, and a spinner would invite one. 0
is the first row, for the reason `PreviewFrameRates` puts its own 0 first —
`Nearest` reads the list as ascending, and a saved 0 must not land on 1 for
being the closest count that exists.

**`None` is no count at all, not a count of nothing.** The loop simply does
not run, nothing is said on the bar, and the take starts on the press —
which is what Record did before 0090, still reachable in one setting.

**The rewind is a checkbox of its own, not another row of the count.**
Folding them into one control would say a count-in and a rewind are degrees
of the same thing. They are not: standing ready and starting from the
beginning are separate things, and a take may want either alone — a
count-in with no rewind to catch a passage as it arrives, a rewind with no
count to start a known patch from the top.

**The two rows go first in the Recording section**, above Frame rate,
Quality and the formats. The section reads in the order a take happens:
counted in, put back to zero, then written. A count-in listed under the
encoder would read as a property of the file.

**`RecordTip` stops naming the behavior and names where it is set.** The tip
is read from the toolbar, where the settings are not visible, and a tip that
promised three seconds would be wrong for anybody who had changed it — the
same trap 0080 avoided by not letting the tip describe a button that was no
longer there.

**A count out of range in the file is clamped, not refused**, between 0 and
ten seconds, the way `FrameRate` and `LatencyMilliseconds` already are: the
file is one somebody may have edited by hand, and a count-in of an hour is a
program that looks broken.

## Consequences

**ADR-0090's three seconds is now a default rather than a rule**, and its
"three seconds, as a constant and not a setting" is superseded. What it
decided about order stands unchanged: counted in, then rewound, then opened,
and each of the three can now be skipped only by the setting that governs it.

**A take can begin four ways**, and all four are reachable from one section:
counted in and rewound (the default), counted in as it stands, rewound at
once, or recording from the press with nothing between.

**The Recording section is six rows now**, and the two added are the only
ones in the settings window that are about a moment rather than a file.

**`CountInAsync` reads `outputSettings` at the moment it counts**, not at the
moment Record was pressed — one more thing the settings window can change
under a take that has not started, and harmless: a count already counting
keeps the number it started on, since the loop has read it.
