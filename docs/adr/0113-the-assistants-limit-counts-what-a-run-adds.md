# ADR-0113: The assistant's limit counts what a run adds

**Status:** Accepted · 2026-09-20 · *user-directed* · amends the limits in
[0065](0065-a-text-language-that-parses-to-a-patch.md); the limit itself
superseded by [0114](0114-the-workbench-does-not-limit-how-large-a-patch-is.md)

## Context

[0065](0065-a-text-language-that-parses-to-a-patch.md) gave the assistant
`write_patch` because the largest preset could not be built one call at a time.
`WorkbenchLimits` then capped a patch at 120 modules in total, and Whole band has
232. `add_module` refused on it and so did `write_patch`, so the one patch that
motivated the language could not be extended at all.

Asked to add a part to Whole band, a model that could not add modules rebuilt the
song smaller with different modules, in 107. Its summary said it had added a
lead. A patch not written in the language loses every module's identity on a
rewrite (ADR-0065), so the original was gone and the person was told otherwise.

## Decision

**The limit is on what a run adds, not on how large the patch is.**
`WorkbenchLimits.MaxAdded` (120) is how many modules a run may add to the patch it
began with, net of the ones it removes. A patch built from nothing behaves as
before; a patch that began past 120 can still be changed.

**The briefing says how to change a patch that is there.** Start from
`describe_patch`. A change to a few modules is the editing tools; a change to many
is `write_patch` with the description altered only where asked, at the price of new
module ids. Everything not asked about stays as it was, and if the change cannot
be made without building something else, say so. Say what was added, removed or
rewired, and say when the request assumed something the patch does not have. Bring
a clipping peak down without taking the whole mix with it.

**The briefing's own example writes `color.hsv`.** Its rule was that `hsv` and
`mix` are written in full, and its example wrote `hsv`, which every run copied and
then had to correct.

## Consequences

**Measured on Whole band.** Three edits that had failed on the old limit completed:
an echo on the lead added three modules and kept every id; adding a second lead
kept all 183 modules exactly as they were and added 29; removing the bass dropped
27 and adjusted two. The last two rewrote the patch, so their ids are new.

**The turn limit is the next ceiling on a large patch.** A structural change to
Whole band is many calls; the exchange limit of one turn still ends one that needs
more, and says so.

**A run may now add 120 to a patch of any size.** The loop that has lost the
thread still stops, at the same distance from where it started.
