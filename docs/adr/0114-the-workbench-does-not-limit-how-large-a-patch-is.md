# ADR-0114: The workbench does not limit how large a patch is

**Status:** Accepted · 2026-09-20 · *user-directed* · supersedes the limit in
[0113](0113-the-assistants-limit-counts-what-a-run-adds.md)

## Context

The module limit existed so that a model which had lost the thread would stop
building. [0113](0113-the-assistants-limit-counts-what-a-run-adds.md) changed it
from a ceiling on the patch to a ceiling on what a run adds, because a ceiling
refused the largest preset. It was still a rule about size.

Nothing about size is what costs. `MaxToolCalls` (200) and the exchange limit of
one turn (40) already end a loop that never stops, and adding a module is a tool
call, so a loop that builds without end meets one of them first. The one thing a
module count alone caught was a single `write_patch` of a huge patch, which is one
call whatever its size and costs the model's output tokens, not modules.

## Decision

**The workbench refuses nothing on a patch's size.** `WorkbenchLimits` no longer
has a module limit, and `add_module` and `write_patch` no longer count modules.
The tool-call and exchange limits are what bound a run.

## Consequences

**A run is bounded by what it spends.** If a runaway ever shows up as a very large
write, it is bounded by the same thing: the calls and the tokens, which is what it
costs, and a limit on those is the one to tighten.

**A limit on size has to be argued for again.** It was chosen without a
measurement, and the first patch it met was the largest preset.
