---
name: build-artifacts
description: Use when the user asks for a local build of main - run release.sh (the Docker gate, publish, pack and sign, into dist/), how long it takes, and what to check before saying it is done. Never run it unasked after a feature lands.
---

# Building the artifacts when asked

`release.sh` run locally is the Release workflow's build, signed with the local test key and
published nowhere: the gate (restore, compile, every test), the self-contained publishes for
the default runtimes (`ARG RIDS` in the Dockerfile: win-x64, osx-arm64, linux-x64) as folders
under `dist/<rid>/`, a signed `SHA256SUMS`, and beside them the `.fbkp` of each plugin the preset site starts with. Run it only when
the user asks for it. `main`'s CI runner is on their machine and builds every push, so a
local build after landing is the same work done twice on one machine. `dist/` is ignored by
git and by the Docker context.

The version is the next minor after the latest tag, and a missing changelog heading for it is
a warning here, not a failure. The build is marked a local one, so its runs count as debug
in the usage statistics, and it trusts only the local key.

## Run it on `main`, in the main worktree

Only ever run `release.sh` in the main worktree with `main` checked out, never in a
`.claude/worktrees/...` worktree and never on a side branch. Anywhere else it builds a commit
that has not landed, and `dist/` ends up in a folder the user does not look in. Fast-forward
`main` first, then check the branch before starting:

```bash
MAIN="$(git rev-parse --path-format=absolute --git-common-dir)/.."
cd "$MAIN" && [ "$(git branch --show-current)" = main ] \
  && ./scripts/release.sh > /tmp/release.log 2>&1; echo "exit $?"
```

If the main worktree is on another branch, it is someone's work in progress: do not switch it.
Say so and leave the build for the user.

Run it in the background: the gate alone is a couple of minutes and the publishes add a few
more, and nothing else in the session waits on it.

## One build at a time: if one is running, report it and stop

Several sessions land on `main` at once and each builds, and a build starts by emptying
`dist/`, so a second build deletes the first one's output from under it. `release.sh` holds
`release.lock` in the git common directory while it runs and refuses to start beside a
live one, exiting 3 with `release: a build is already running (pid …, since …)`.

Look before starting:

```bash
lock="$(git rev-parse --path-format=absolute --git-common-dir)/release.lock"
[ -d "$lock" ] && kill -0 "$(cat "$lock/pid")" 2>/dev/null \
  && echo "running since $(cat "$lock/since")"
```

If a build is running, or `release.sh` exits 3, tell the user a build is already running
and since when, and do not start a second: no retry, no waiting loop, no build afterwards.
A build that started before this commit landed does not hold it, so say that too; the next
session to land, or the user, builds a `main` that does.

## Read the whole log, then filter

Capture the output to a file and grep it afterwards, never in the pipe: a `tail` or a narrow
grep drops the per-test `failed <name>` lines and the run has to be repeated to learn what
broke. A gate failure means the commit on `main` does not pass the suite in a clean tree,
which is news the user needs in the first sentence, with the failing test's name.

## Say what is there

When it finishes, say which runtimes are under `dist/`, that they are the build of the
commit named, and nothing more. Do not launch the built app: `running-the-app.md` still
applies, and the build is the user's to run.
