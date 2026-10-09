# Continuous integration and delivery

Continuous integration is a practice, not a server: changes land on `main` small and often, every change is verified by the whole suite, and a broken `main` is fixed before anything else. Continuous delivery follows from it: `main` is always releasable, so a release is `/release`, not a project. The pipeline that checks it is in `pipeline.md`; this is the discipline it serves.

## Integrate small and often

- Several sessions land on `main` at once, so integrating late is where they collide. A worktree's branch lives hours, not days: rebase onto `main` whenever it moves and land as soon as a step is whole (`git-workflow.md`).
- Each commit is one small, whole step that builds and passes on its own. A large change lands as a sequence of such steps, not one drop at the end.
- A commit that CI has not seen is not integrated. Push what landed on `main`, unless the user said to hold it.

## Every change is verified

- The classes a change touches while working; the full suites before each commit (the `tests` skill).
- Read the Build workflow's verdict on each push rather than assuming it: `gh run list --commit <sha>`. A change is integrated when `main` is green with it.
- A test that fails some runs and not others is a broken build that happens to pass sometimes. Fix it; never rerun until green (`honest-tests.md`).

## A red main stops the line

- When Build goes red on `main`, fixing it is the next thing: before the task in hand, before any new commit. Say so in the first sentence of the reply.
- Fix forward if the fix is quick and obvious; otherwise revert the commit that broke it, in a commit of its own, and fix it in a worktree.
- Never land on a red `main`. A second commit on a red build hides which one broke it.
- Red on another session's commit is still red. Say whose commit it is, and fix it or revert it; do not build on it.

## main is always releasable

- Every commit on `main` could be released as it stands: `/release` may be run on any of them.
- Unfinished work lands dark: a module not in the catalog, a setting not in the dialog, a command not in `flyback-cli`'s help, until it is whole. Never parked on a long-lived branch, and never visible half-done.
- What goes with a change lands in the same commit, so a release needs no catch-up: the `## Unreleased` bullet (the `changelog` skill), the site edit (the `website` skill), the migration of the preset site's defaults, `PublicAPI.Unshipped.txt` when the contract moved, and from 1.0.0 the upgrade step for saved patches (`saved-data.md`).

## A release is a non-event

- Release on demand, small and often; each release is a minor unless there is a reason.
- The build that passed is the build that ships: `release.sh` runs the gate and publishes from the same image, and nothing is rebuilt between the two.
- The website is deployed continuously: `worker.yml` deploys `site/` with the Worker on every push to `main` that touches it, once Build has passed on that commit. That is why a site edit lands with the change it describes; there is no release step to catch a stale page.
