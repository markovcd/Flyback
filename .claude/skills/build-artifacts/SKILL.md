---
name: build-artifacts
description: Use after a feature has landed on main - run make.sh (the Docker gate and publish, into artifacts/) so the user gets the build without asking, how long it takes, and what to check before saying it is done.
---

# Building the artifacts after a feature lands

`make.sh` is `docker build --output artifacts .`, built to trust the local `RELEASE_SIGNING_KEY`: the gate (restore, compile, every
test) and then the self-contained publishes for the default runtimes (`ARG RIDS` in the
Dockerfile: win-x64, osx-arm64, linux-x64) into `artifacts/<rid>/`. The user wants this run
unprompted whenever a feature lands on `main`, so the build of what landed is on disk under
`artifacts/` when they come back. `artifacts/` is ignored by git and by the Docker context.

## Run it from the main checkout

Run it once `main` has been fast-forwarded, from the main checkout rather than a worktree, so
what is built is the commit that landed and the output is where the user looks:

```bash
MAIN="$(git rev-parse --path-format=absolute --git-common-dir)/.."
cd "$MAIN" && ./make.sh > /tmp/make.log 2>&1; echo "exit $?"
```

Run it in the background: the gate alone is three to four minutes and the publishes add a
few more, and nothing else in the session waits on it. One build at a time; a second
`docker build` beside it fights for the same cores and the same cache.

## Read the whole log, then filter

Capture the output to a file and grep it afterwards, never in the pipe: a `tail` or a narrow
grep drops the per-test `failed <name>` lines and the run has to be repeated to learn what
broke. A gate failure means the commit on `main` does not pass the suite in a clean tree,
which is news the user needs in the first sentence, with the failing test's name.

## Say what is there

When it finishes, say which runtimes are under `artifacts/`, that they are the build of the
commit named, and nothing more. Do not launch the built app: `running-the-app.md` still
applies, and the artifacts are the user's to run.
