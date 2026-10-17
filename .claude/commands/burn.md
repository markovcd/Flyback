---
description: Spend spare tokens on code quality and stability - pick the burn item that ran longest ago, do one whole piece of it, land it, and log it.
---

# Burn

Spare tokens at the end of a plan's window are worth more as a cleaner, sturdier
codebase than as nothing. Each run picks one item below, does one whole piece of
it, lands it on `main`, and logs it in `docs/agents/burn-log.tsv`. The user asked
for the work by typing `/burn`, so an item that fixes is done, not proposed.

$ARGUMENTS names an item (`/burn flakes`) or a subsystem to aim it at
(`/burn hacks Editor`). With nothing, choose.

## Choosing

1. Read `docs/agents/burn-log.tsv`. Take the item whose last run is oldest, or
   one never run, in list order. `hacks` first while it has never run.
2. Read `git log --oneline -30 origin/main` and `git worktree list`. Stay out of
   files another session is working in, and out of anything `TODO.md` says a
   thread is building; pick the next item rather than collide.
3. One piece per run. A run that finds more than it can finish lands what it
   finished and lists the rest in its log row's note, or in `TODO.md` when it is
   real work.

## The items

- **hacks** - find the hackiest code in `src/`: name or text matching where a
  reference would do, reflection, unnamed indices, a branch on one module's or
  preset's name, a sleep waiting on a state nothing reports, a swallowed error
  hiding a real one. Rank by harm, fix the worst, and test the rule it broke.
- **bughunt** - run `/bughunt` on the subsystem hunted longest ago (the log's
  notes say which).
- **mutate** - run `/mutate` on the most recent `src/` commit with no row in
  `tests/mutation-log.tsv`.
- **slow** - rank the suite by duration (the `tests` skill) and fix the slowest
  test whose time nobody explained, or explain it in a comment beside it.
- **flakes** - run one test project ten times in a row; a test that fails any
  run is a bug in the test or the code. Find which and fix it.
- **twice** - find one feature's logic written in two places (the editor and
  the web editor, the CLI and the viewer, two backends) and pull it into one.
- **dead** - delete `src/` members nothing calls, and shrink
  `tools/TestOnlyMembers/baseline.tsv` by making a test-only member unneeded.
- **coverage** - run `scripts/coverage.sh`, take the least-covered file that
  holds real logic, and test the rules it states.
- **doors** - pick one place outside input enters (`docs/threat-model.md`) and
  feed it hostile input: truncated, oversized, wrong type, a path that climbs
  out. Every throw or hang is a bug; fix it at the door.
- **drift** - find a statement in `docs/`, a skill, a command or `site/` that is
  no longer true of the code, and fix whichever side is wrong.
- **warnings** - build with warnings shown and clear one kind of them, without
  a `#pragma` or `NoWarn` unless a comment says why it holds.
- **split** - split the largest file in `src/` that holds several types or does
  several jobs (`one-type-per-file.md`), verbatim, one type per file.
- **errors** - pick a command or dialog and make each of its failures say where
  and what, the way `agent-drivability.md` asks.

## Landing

- Follow the standing rules: `main` directly, isolation from other sessions'
  work, honest tests, the changelog only for what a user notices.
- Full Core and Plugins suites, plus the project the change lives in, before the
  commit. Push, then read Build's verdict on the commit.
- Append one tab-separated row to `docs/agents/burn-log.tsv`: today's date, the
  item, and a note of what was done and what was left. The row rides in the
  run's commit, so `git log -- docs/agents/burn-log.tsv` finds the change; a run
  that found nothing commits its row alone.

## Reporting

Lead with what changed and why it was the worst of what was found. Then the
runners-up the run did not get to, a line each, so the next `/burn` can take
them.
