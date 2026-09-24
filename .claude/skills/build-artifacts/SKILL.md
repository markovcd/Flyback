---
name: build-artifacts
description: Use after a feature has landed on main - run release.sh (the Docker gate, publish, pack and sign, into dist/) so the user gets the build without asking, how long it takes, and what to check before saying it is done.
---

# Building the artifacts after a feature lands

`release.sh` run locally is the Release workflow's build, signed with the local test key and
published nowhere: the gate (restore, compile, every test), the self-contained publishes for
the default runtimes (`ARG RIDS` in the Dockerfile: win-x64, osx-arm64, linux-x64) as folders
under `dist/<rid>/`, the Figures package, and a signed `SHA256SUMS`. The user wants this run
unprompted whenever a feature lands on `main`, so the build of what landed is on disk under
`dist/` when they come back. `dist/` is ignored by git and by the Docker context.

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
  && ./release.sh > /tmp/release.log 2>&1; echo "exit $?"
```

If the main worktree is on another branch, it is someone's work in progress: do not switch it.
Say so and leave the build for the user.

Run it in the background: the gate alone is a couple of minutes and the publishes add a few
more, and nothing else in the session waits on it. One build at a time; a second
`docker build` beside it fights for the same cores and the same cache.

## Read the whole log, then filter

Capture the output to a file and grep it afterwards, never in the pipe: a `tail` or a narrow
grep drops the per-test `failed <name>` lines and the run has to be repeated to learn what
broke. A gate failure means the commit on `main` does not pass the suite in a clean tree,
which is news the user needs in the first sentence, with the failing test's name.

## Say what is there

When it finishes, say which runtimes are under `dist/`, that they are the build of the
commit named, and nothing more. Do not launch the built app: `running-the-app.md` still
applies, and the build is the user's to run.
