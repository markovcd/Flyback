# ADR-0212: macOS runs on every push; the device runs and the measuring run daily

**Status:** Accepted · 2026-10-24 · *user-directed* · supersedes
[0200](0200-macos-runs-by-hand-and-before-a-release.md) and
[0195](0195-ci-runs-on-githubs-runners-on-every-os-self-hosted-by-hand.md)

## Context

ADR-0200 took macOS off every push because GitHub billed its minutes. The repository is public,
and GitHub's standard hosted runners, macOS included, are free and unlimited for a public
repository; only larger runners are billed, and Flyback uses none. Cost no longer keeps anything
off a run. Several checks ran only by hand or weekly: the Android emulator smoke, the iOS
simulator run, Coverage and Test-only members.

## Decision

**Every job runs on GitHub's standard runners, and none on a self-hosted one.** The user's
machines are no longer runners: `runner.yml`, the `self-hosted` inputs of Build, Coverage and
Release, `ios.yml`'s `machine` choice and the `RUNNER_STATUS_TOKEN` secret are gone, and with
them the gate's Windows path and the rule that kept forks off the user's machines.

**Every push runs the native tests on Windows and macOS**, on `windows-latest` and
`macos-latest`, with no box to tick.

**The device runs are daily.** The Cloud gate's Android emulator job and `ios.yml`'s simulator
run are scheduled once a day on `main`, on GitHub's machines, as well as by hand. Neither goes on
every push: the emulator is slow and has been flaky, and the iOS editor is not released.

**Coverage and Test-only members are daily**, in place of Monday and Tuesday.

**A scheduled run that fails opens an issue**, or comments on the open one of its title, through
`tell.yml`, since nobody watches a scheduled run.

## Consequences

- A break only macOS shows is named on the push that made it.
- `/release` still refuses a commit without a green Android smoke; a daily run on the tip
  satisfies it when nothing has landed since.
- An iOS break is found within a day rather than when someone thinks to run it.
- The gate stays one Linux job; sharding it is a separate decision, not taken here.
- A run waits for GitHub's queue, never for a machine of the user's to wake.
- If the repository went private, macOS and the daily runs would be billed again.
