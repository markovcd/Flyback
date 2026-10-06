# Handoff

Write-ups too long for a line in [TODO.md](../../TODO.md): a plan, an audit, or a problem found while working on something else. Each file is one piece of work, with what is wrong or wanted, the evidence, and the fix. Its TODO.md item links to it; take the item off, and delete the file, in the commit that lands the last of it.

| File | What | Kind | Status |
|---|---|---|---|
| [decision-model.md](decision-model.md) | A decision model behind the plugin boundary | Plan | Open |
| [formulas-into-modules.md](formulas-into-modules.md) | What the presets still write as formulas, and which modules would replace it | Audit | Open |
| [sync-to-async.md](sync-to-async.md) | Blocking work on the UI thread, and what to do about each | Audit | Open |
| [assistant-request-cost.md](assistant-request-cost.md) | The assistant spends requests and tokens it does not need | Issue | Open: four of eight fixed |
| [grab-the-output.md](grab-the-output.md) | Grab the picture or the sound and the knobs move: derivatives through the patch | Plan | Open, parked; spiked |
| [android-editor.md](android-editor.md) | An Android editor: a third shell beside the desktop and the page | Plan | Open, parked |
| [ios-editor.md](ios-editor.md) | An iPhone and iPad editor: what differs from Android | Plan | Open, parked |
| [touch-bugs.md](touch-bugs.md) | What a finger still cannot do, on the desktop and in the page | Issue | Open: 7 of 20 fixed, the rest not yet checked on a phone |

Severity, for an issue: **Critical**, act now; **High**, a security or data problem in a release; **Medium**, wrong behavior or real risk; **Low**, friction or latent risk.

## Adding one

A problem found out of scope is said in the reply first. When the user wants it kept, write it here, add its row above, and add a TODO.md item that links to it.

Name the file for the work (`touch-bugs.md`), not a number. Open with the date, the commit it was written against, and the line saying it is on TODO.md and when to delete it. For an issue, this outline:

```markdown
# Title stating what is wrong

Found on YYYY-MM-DD, on `main` at `<commit>`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Severity:** Critical | High | Medium | Low
- **Status:** Open | In progress (branch) | Done (commit)

## What is wrong
## Evidence
## Impact
## Suggested fix
```

Say what was confirmed by running it and what only by reading the code.
