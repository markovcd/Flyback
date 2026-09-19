# Prose style

## American spelling

Write American spellings, not British: "color" not "colour", in code, comments, docs and commit messages.

The codebase's identifiers already use it (`Colors`, `PortColor`, `ColorPort`, `NodeCatalog.Color`), so British spellings in the surrounding prose make comments disagree with the code they describe. It covers prose as much as identifiers: doc comments, ADR bodies and commit messages, not only symbol names. Check before committing any file with a lot of new prose, since this repo's comments run long.

## Comments never narrate history

Keep comments short. Explain a thing only where it is not obvious, and then in a sentence or two. Never describe how the code used to be or what a change did: no "three rather than six", "unchanged by the consolidation", "ten thousand did not hold that".

**Why:** long prose buries the rule it is stating, and the history already lives in git and `docs/adr/`.

**How to apply:** write the comment as if the current shape were the only shape it ever had, and cut it to the shortest form that still carries the non-obvious part. Prefer one `<summary>` line; reach for `<remarks>` only when a real surprise needs justifying. Extended reasoning belongs in an ADR under `docs/adr/`. The same rule holds for commit messages.
