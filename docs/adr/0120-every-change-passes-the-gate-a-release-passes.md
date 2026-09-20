# ADR-0120: Every change passes the gate a release passes

**Status:** Accepted · 2026-09-20

## Context

Nothing ran the tests. `.github/workflows/` held two workflows — Pages, on a
push that touches `site/`, and Release, on a dispatch from the Actions tab —
and neither is a push to `main` or a pull request. The suite is about seven
thousand tests across ten projects, and the only things that ran it were a
person typing `dotnet test` and the Dockerfile, inside a release.

A release is the worst moment to find out. The tests are the third step of a
build that ends in a signed set of per-platform artifacts, so a failure there
is a release that did not happen, found by somebody who came to publish one,
about a commit that may be weeks old.

The obvious fix is a workflow that installs an SDK and runs `dotnet test`. What
that costs is a second description of the environment: the headless UI tests
rasterize with libSkiaSharp and will not load it without fontconfig beside it,
Attention wants libX11 present to call into, and the tests that write an MP4
skip themselves when there is no ffmpeg on PATH. The Dockerfile already names
those three and says why. A workflow naming them again is a list that goes
stale silently — the tests it protects skip rather than fail.

## Decision

**CI builds the Dockerfile.** `.github/workflows/ci.yml` runs
`docker build --target gate .` on every push to `main`, every pull request and
on demand. There is one description of what a build needs and one description of
what a change has to pass.

**The Dockerfile's first stage is the gate**, named for it: restore, compile,
and `dotnet test --solution`. Publishing moved to a `publish` stage on top of
it, and the artifacts stage copies from there. The release build is unchanged —
it asks for the last stage and gets all of them — while CI asks for the first
and stops after the tests, which is where the three self-contained publishes
that dominate the build would otherwise start.

## Consequences

**A red commit is found on the commit.** The gate that used to stand in front of
a release now stands in front of `main`, and the release's copy of it becomes a
formality rather than the first run.

**Runs are slower than a native workflow's**, and start cold. A GitHub runner
keeps nothing between jobs, so the SDK image, the apt packages and every NuGet
package are fetched each time; the cache mounts in the Dockerfile buy nothing
here. Skipping the publishes is what pays for it.

**A run is cancelled by the next push to the same ref**, since the older one is
testing a commit nobody is waiting on.

**Neither gate can drift from the other**, which is the point. A library the
tests need is added to the Dockerfile once and both have it.
