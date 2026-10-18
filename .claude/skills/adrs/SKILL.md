---
name: adrs
description: Use before proposing a refactor, or when writing or editing an ADR under docs/adr/ - where the refactors the ADRs already declined are listed, how to tell real drift from a pattern that was never extended, when to rewrite a day-old ADR in place versus add a dated amendment, and how to number a new one at commit time.
---

# ADRs

## Check them before proposing a refactor

The ADRs in `docs/adr/` are not background reading: they decline the refactors a scan would otherwise surface first. The ones a first scan finds are listed in the engineering guide's [Refactors that are already declined](../../../docs/engineering-guide.md#14-refactors-that-are-already-declined); read that list before proposing anything, and add to it when an ADR declines a new one.

Proposing these reads as not having done the reading, and the rationale comments are usually better than the reasoning that would replace them.

The refactors worth proposing are the ones where code has drifted away from a decision the repo made (ADR-0017 claims the node editor is 477 lines; it is 2708), or where an established in-repo pattern was never extended. Grep for the drift before proposing it: a pattern that looks unfinished may already have been carried through (`Text.cs` did for font sizes what `Colors.cs` did for colors). Rank by code lines, not total: comment density runs 31-65%, so a 905-line preset can be 381 lines of code.

## Rewrite a day-old ADR in place

An ADR written in the last day or so is the same decision still being settled, so edit its text and let the new wording stand as if it had always read that way. No `## Amendment` section, no note of what it used to say, no second ADR superseding it; those are for decisions that have sat on `main` long enough to be read and built on.

Check the date with `git log -1 --format=%ci -- docs/adr/<file>`. Within 24 hours, overwrite the affected paragraphs, including the heading and the README index row if the decision itself moved. Older than that, add the dated amendment section the repo already uses.

## Number a new ADR at commit time

Several sessions write ADRs on `main` at once, and two have collided on a number (0103 exists twice; a drafted 0105 had to become 0106). Before committing a new ADR, run `git ls-tree --name-only main docs/adr/` for the highest number, then fix the file name, its heading, the README index row and any links (`docs/language.md` links ADRs too).
