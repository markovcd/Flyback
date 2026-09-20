# ADR-0113: The workbench does not limit how large a patch is

**Status:** Accepted · 2026-09-20 · *user-directed* · amends the limits in
[0065](0065-a-text-language-that-parses-to-a-patch.md)

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

A ceiling on what one run adds would have let that edit through, and it is still a
rule about size. Nothing about size is what costs. `MaxToolCalls` (200) and the
exchange limit of one turn (40) already end a loop that never stops, and adding a
module is a tool call, so a loop that builds without end meets one of them first.
The one thing a module count alone caught was a single `write_patch` of a huge
patch, which is one call whatever its size and costs the model's output tokens,
not modules.

## Decision

**The workbench refuses nothing on a patch's size.** `WorkbenchLimits` has no
module limit, and neither `add_module` nor `write_patch` counts modules. The
tool-call and exchange limits are what bound a run.

**The briefing says how to change a patch that is there.** Start from
`describe_patch`. A change to a few modules is the editing tools; a change to many
is `write_patch` with the description altered only where asked, at the price of new
module ids. Everything not asked about stays as it was, and if the change cannot
be made without building something else, say so. Say what was added, removed or
rewired, and say when the request assumed something the patch does not have. Bring
a clipping peak down without taking the whole mix with it.

**The briefing's own example writes `color.hsv`.** Its rule is that `hsv` and
`mix` are written in full, and its example wrote `hsv`, which every run copied and
then had to correct.

## Consequences

**Measured on Whole band.** Three edits that a ceiling of 120 refused complete:
an echo on the lead added three modules and kept every id; adding a second lead
kept all 183 modules exactly as they were and added 29; removing the bass dropped
27 and adjusted two. The last two rewrote the patch, so their ids are new.

**The turn limit is the next ceiling on a large patch.** A structural change to
Whole band is many calls; the exchange limit of one turn still ends one that needs
more, and says so.

**A run is bounded by what it spends.** If a runaway ever shows up as a very large
write, it is bounded by the same thing: the calls and the tokens, which is what it
costs, and a limit on those is the one to tighten.

**A limit on size has to be argued for again.** It was chosen without a
measurement, and the first patch it met was the largest preset.
