# ADRs

## Check them before proposing a refactor

Flyback keeps about 70 ADRs in `docs/adr/`. They are not background reading: they pre-emptively decline the refactors a scan would otherwise surface first.

- MVVM / view models for `MainWindow`: declined by ADR-0016 and again by ADR-0039.
- Unifying the interpreter / GLSL / `OpShape` opcode switches: ADR-0035 requires the transcription, because GLSL builtins disagree with `CompiledPatch.Evaluate`, which is the specification. Already guarded by an opcode-coverage test.
- Sharing the Gemini/OpenAi session tool loops: documented as deliberate in `GeminiSession.cs`'s own header.

Proposing these reads as not having done the reading, and the rationale comments are usually better than the reasoning that would replace them.

The refactors worth proposing are the ones where code has drifted away from a decision the repo made (ADR-0017 claims the node editor is 477 lines; it is 2708), or where an established in-repo pattern was never extended (`Colors.cs` solved color tokens and stopped, leaving 92 `FontSize` literals). Rank by code lines, not total: comment density runs 31-65%, so a 905-line preset can be 381 lines of code.

## Rewrite a day-old ADR in place

An ADR written in the last day or so is the same decision still being settled, so edit its text and let the new wording stand as if it had always read that way. No `## Amendment` section, no note of what it used to say, no second ADR superseding it; those are for decisions that have sat on `main` long enough to be read and built on.

Check the date with `git log -1 --format=%ci -- docs/adr/<file>`. Within 24 hours, overwrite the affected paragraphs, including the heading and the README index row if the decision itself moved. Older than that, add the dated amendment section the repo already uses.

## Number a new ADR at commit time

Several sessions write ADRs on `main` at once, and two have collided on a number (0103 exists twice; a drafted 0105 had to become 0106). Before committing a new ADR, run `git ls-tree --name-only main docs/adr/` for the highest number, then fix the file name, its heading, the README index row and any links (`docs/language.md` links ADRs too).
