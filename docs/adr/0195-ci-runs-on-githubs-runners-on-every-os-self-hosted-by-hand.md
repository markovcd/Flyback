# ADR-0195: CI runs on GitHub's runners on every OS; the self-hosted ones are by hand

**Status:** Superseded by [0212](0212-macos-runs-on-every-push-the-device-and-measuring-runs-daily.md) · 2026-10-12 · *user-directed*

## Context

Build, Coverage and Release ran on `main` with `runs-on: [self-hosted, X64]`, so
a run waited for either of the user's two machines. With both asleep, `main`
sat queued: nothing was tested, Android and Worker waited behind it, and no
release could be cut. And the gate is a Linux image, so the tests only Windows
or only macOS can run ran nowhere.

The user asked for CI that needs none of his machines, verifying Linux,
Windows, macOS and Android, with the self-hosted runners kept as an option.

## Decision

**Every push runs on GitHub's runners.** Build runs the gate on `ubuntu-latest`
and, beside it, the solution's tests natively on `windows-latest` and
`macos-latest` (`native-tests.yml`). The native legs leave the browser and Node
tests to the gate, since those run the same everywhere. Coverage and Release run
on `ubuntu-latest`.

**The self-hosted runners are a manual option.** Build, Coverage and Release take
a `self-hosted` input when started by hand on `main`. `runner.yml` then picks the
first online runner in the order Linux, Windows, GitHub's, read through the
`RUNNER_STATUS_TOKEN` secret (a fine-grained token with Administration read on
this repository only, since `GITHUB_TOKEN` cannot list runners). Without the
token the run waits for either self-hosted runner. Any other ref gets GitHub's,
so a fork never reaches a self-hosted machine.

**The Android emulator runs before a release, not per push.** `android.yml`
builds the APK on every push; the Cloud gate (`cloud-gate.yml`, by hand) starts
it on an emulator, and `/release` refuses a commit that smoke has not passed on.
An emulator on every push would mostly add flakes.

## Consequences

**`main` is verified with both machines off**, on four operating systems. A push
waits for the slowest leg, about fifteen minutes on Windows or macOS against
seven for the gate on a warm self-hosted cache. Free on a public repository.

**Windows needs Mesa's software OpenGL** beside the test hosts (pinned by
version and hash). macOS's CGL context opens only with `FLYBACK_CGL=1`, which
the Cloud gate's `macos-gl` box sets, so Build skips its `Gpu` tests.

**The self-hosted builder's cache goes cold** unless a run is started there by
hand.

**Amended 2026-10-15:** macOS no longer runs on every push; Build runs it when its `macos` box is ticked, and `/release` waits for it ([0200](0200-macos-runs-by-hand-and-before-a-release.md)).

**Amended 2026-10-21:** `FLYBACK_CGL` stays opt-in. GitHub's Mac has no GPU, and Apple's software renderer draws seven presets unlike the processor; on a real Apple GPU the same seven agree with it to within 1 of 255, apart from 8 pixels on the Probe's saw edges. The `macos-gl` box is for a run on a real Mac.
