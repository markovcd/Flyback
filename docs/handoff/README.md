# Handoff

Write-ups too long for a line in [TODO.md](../../TODO.md): a plan, an audit, or a problem found while working on something else. Each file is one piece of work, with what is wrong or wanted, the evidence, and the fix. Its TODO.md item links to it; take the item off, and delete the file, in the commit that lands the last of it.

| File | What | Kind | Status |
|---|---|---|---|
| [decision-model.md](decision-model.md) | Laya, and what to ask a decision model, measured against a local laya-serve | Plan | Open: all but Laya landed; next steps proposed |
| [assistant-says-what-it-can-do.md](assistant-says-what-it-can-do.md) | The assistant answers what it can do, and offers plugin authoring when a .NET SDK is present | Plan | Open |
| [assistant-request-cost.md](assistant-request-cost.md) | The assistant spends requests and tokens it does not need | Issue | Open: four of eight fixed |
| [grab-the-output.md](grab-the-output.md) | Grab the picture or the sound and the knobs move: derivatives through the patch | Plan | Open, parked; spiked |
| [android-editor.md](android-editor.md) | An Android editor: a third shell beside the desktop and the page | Plan | Open, parked |
| [ios-editor.md](ios-editor.md) | An iPhone and iPad editor: what differs from Android | Plan | Open, parked |
| [frame-sequences.md](frame-sequences.md) | A Path that plays a numbered sequence of drawings, a frame at a time, so the animation is the music | Plan | Open |
| [youtube-videos.md](youtube-videos.md) | Videos for the YouTube channel: how one is made, what was made, what is proposed | Backlog | Open, grows |
| [tests-audit.md](tests-audit.md) | Where the tests are weakest, and what would fix each point | Audit | Open: what is left needs a seam, a workload or a fixture |
| [pipeline-audit.md](pipeline-audit.md) | Where the pipeline is weakest, and what would fix each point | Audit | Open |
| [asio-amd-crash.md](asio-amd-crash.md) | Rain on a Window on ASIO4ALL crashes inside AMD's OpenGL driver | Issue | Open: needs a session on the Windows machine |
| [nested-def-boxes.md](nested-def-boxes.md) | Showing a def called inside a def, inside the outer call's box | Plan | Open, parked; no option chosen |
| [tests-on-github-hosted-runners.md](tests-on-github-hosted-runners.md) | Every test on GitHub's runners: Windows, macOS and Android legs beside the gate | Plan | Open |
| [microsoft-store.md](microsoft-store.md) | Flyback Modular on the Microsoft Store: the submission steps left to the user | Plan | Open: waits on the user |

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
