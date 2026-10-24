# ADR-0200: macOS runs by hand and before a release, not on every push

**Status:** Superseded by [0212](0212-macos-runs-on-every-push-the-device-and-measuring-runs-daily.md) · 2026-10-15 · *user-directed* · amends [0195](0195-ci-runs-on-githubs-runners-on-every-os-self-hosted-by-hand.md)

## Context

ADR-0195 put the native tests on `macos-latest` in every Build. GitHub bills a
macOS minute at about ten times a Linux one, and on 2026-10-14 the macOS leg
was $17.30 of the day's $27.16 of Actions. The user asked for it to run on
demand.

## Decision

**A push runs the gate on Linux and the native tests on Windows.** Build passes
`native-tests.yml` the machines `["windows"]` unless its `macos` box is ticked
under Run workflow, which adds `macos`. The Cloud gate's `macos` and `all` still
run it.

**A release still waits for macOS.** `/release` refuses a commit on which no
Build or Cloud gate run has a green `native / test (macos)` job, as it does for
the Android smoke.

## Consequences

**A break only macOS shows is found later**: at the next ticked run or at
`/release`, not on the push that made it. The macOS-only tests are CoreAudio,
CoreMIDI and the Keychain, which change rarely.

**A release costs one macOS run** more than a push, started by hand.

`OperatingSystemLegTests` holds Build to Windows on every push and macOS behind
its box, and the Cloud gate's `all` to both.

**Amended 2026-10-21:** when Build runs macOS on `main`, it runs on the user's
self-hosted Mac (`[self-hosted, macOS, ARM64]`) if `runner.yml` finds it online,
whether or not the `self-hosted` box is ticked, and on `macos-latest` otherwise.
An offline Mac is never waited for. Build therefore passes `RUNNER_STATUS_TOKEN`
to every run on `main`. The Cloud gate stays on GitHub's machines.

**Amended 2026-10-21, user-directed:** while the Mac is online, every push to
`main` runs macOS on it, since it costs nothing; Build's `machines` adds `macos`
when `runner.yml` reports `mac-online`. A push made while the Mac is off runs
Windows only, as before, and `/release` still waits for a green macOS job.
