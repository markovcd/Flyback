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

## Run it from the main checkout

Run it once `main` has been fast-forwarded, from the main checkout rather than a worktree, so
what is built is the commit that landed and the output is where the user looks:

```bash
MAIN="$(git rev-parse --path-format=absolute --git-common-dir)/.."
cd "$MAIN" && ./release.sh > /tmp/release.log 2>&1; echo "exit $?"
```

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
