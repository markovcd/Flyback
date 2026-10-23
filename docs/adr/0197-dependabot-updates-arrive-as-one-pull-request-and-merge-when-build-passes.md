# ADR-0197: Dependabot's updates arrive as one pull request and merge when Build passes

**Status:** Accepted · 2026-10-14 · *user-directed* · builds on [0195](0195-ci-runs-on-githubs-runners-on-every-os-self-hosted-by-hand.md)

## Context

Dependabot opened a pull request per NuGet package, and its updater rewrites
only some of the `packages.lock.json` files a version reaches. Three such pull
requests were merged on one evening; each was stale against the others' lock
files, locked restore failed on nine projects, and Build went red on `main`.

The user asked for the updates to land on their own. Dependabot cannot push to
`main`; it only opens pull requests.

## Decision

**Every NuGet update arrives in one grouped pull request a week**
(`.github/dependabot.yml`). Nothing it carries conflicts with a sibling, and the
lock files move once.

**`dependabot-prs.yml` rewrites the lock files** on a NuGet pull request: it
restores the branch with `--force-evaluate` under a read-only token, then a
second job, which runs nothing from the branch, commits the lock files that
moved, and only those, back to it.

**The same workflow merges** a Dependabot pull request once Build passes on its
head: it squashes it into `main`. It does this itself rather than through
GitHub's auto-merge, which would need a required status check on `main`, and a
required check would refuse every direct push the trunk workflow makes.

**It starts Build by hand after each write.** A push or a merge made with
`GITHUB_TOKEN` starts no workflow, so the lock push dispatches Build on the
branch and the merge dispatches it on `main`. Build treats every run on `main`
as it treats a push (its own concurrency group, never cancelled), and Android and
Worker follow a passing run started by hand on `main` as they follow a push.

**GitHub Actions bumps are merged by hand.** `GITHUB_TOKEN` cannot change a
workflow file. Docker bumps merge too: the Dockerfile's `FROM` is the one place
an image is named, and every workflow or script that runs one reads it from there
with `scripts/base-image.sh` (`BaseImageTests`).

## Consequences

**An update lands unread.** Build is the only check between a package's new
version and `main`. A red Build leaves the pull request open.

**Dependabot stops rebasing a pull request the workflow pushed to.** One that
falls behind `main` and conflicts waits for `@dependabot recreate`. With one
NuGet pull request a week this is rare.

**No secret and no repository setting.** Everything runs on `GITHUB_TOKEN`; the
cost is the two dispatches and Android and Worker accepting a Build started by
hand.
