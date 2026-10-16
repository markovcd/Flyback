# ADR-0206: The gate has a ten-minute time budget

**Status:** Accepted · 2026-10-16 · *user-directed* · extends
[0120](0120-every-change-passes-the-gate-a-release-passes.md)

## Context

The gate runs every test on every push, about 16,000 in five minutes. The suite
grows with the code, and a gate that takes half an hour is one people start
working around. Nothing said how long it took until it was already slow.

## Decision

**The test run has a budget of ten minutes, and the summary says where it went.**
The `tested` stage keeps the run's wall-clock seconds beside the exit code.
`scripts/test-summary.py` prints them against the budget, lists each project's
time with the slowest first, and past the budget prints an over-budget line and
a workflow warning. The budget warns; it never fails the gate, which fails only
on what is wrong.

Over budget, the next step is sharding the gate by test project across parallel
jobs, each still running every test in it. Running only the tests a change can
reach is not the next step: a push to `main` has no other check, and a missed
dependency would land red unseen.

## Consequences

- A slow gate shows up as a number on every run, long before it is a habit.
- Per-project times come from the JUnit reports, so the shard split can be
  planned from any recent run.
- A run served whole from the layer cache shows the time of the run it cached.
