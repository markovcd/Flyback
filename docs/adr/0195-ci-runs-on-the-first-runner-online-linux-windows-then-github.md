# ADR-0195: CI runs on the first runner online: Linux, Windows, then GitHub's

**Status:** Accepted · 2026-10-12 · *user-directed*

## Context

Build, Coverage and Release ran on `main` with `runs-on: [self-hosted, X64]`, so
a run waited for either of the user's two machines. With both asleep, `main`
sat queued: nothing was tested, Android and Worker waited behind it, and no
release could be cut. The user asked for a priority list: the Linux machine,
then the Windows one, then GitHub's runners.

GitHub Actions has no fallback between runners. A job names labels and waits
for a runner that has them; it never gives up on one and tries another.

## Decision

**A first job picks the runner.** `.github/workflows/runner.yml` is a reusable
workflow on `ubuntu-latest` that lists the repository's runners and returns the
first online one in the order Linux, Windows, GitHub's, as a `runs-on` value
the heavy job reads through `fromJSON`. Any ref but `main` goes to GitHub's
without asking, so a fork never reaches a self-hosted machine.

**It reads with its own token.** Listing runners needs Administration read,
which `GITHUB_TOKEN` cannot be granted, so the secret `RUNNER_STATUS_TOKEN` is a
fine-grained token scoped to this repository with that one permission. It is
passed only on `main`. Without it the pick is either self-hosted runner, as
before; with it and the API failing, the pick is GitHub's, since a run that
goes somewhere beats one that waits.

**The gate is the same everywhere.** It is the Docker image (ADR-0120), so the
three runners differ only in cache: a self-hosted runner keeps its capped
builder, GitHub's uses GitHub's layer cache, as pull requests already did.

## Consequences

**`main` is tested with both machines off**, at the cost of a cold GitHub
runner: eleven minutes for Build against seven, longer for Release. Timeouts
allow for it.

**A busy runner still wins.** Online is the test, not idle; a second push
queues behind the first on Linux rather than moving to Windows.

**A runner that drops between the pick and the job leaves the job waiting** for
labels nothing serves, until it comes back or GitHub's 24 hours run out.

**Every run costs one short GitHub-hosted job**, free on a public repository.
