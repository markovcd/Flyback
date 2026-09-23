---
name: build-artifacts
description: Use after a feature has landed on main - run what make.sh does (the Docker gate and publish, into artifacts/) so the user gets the build without asking, how to run it on this machine where the repo sits in ownCloud, how long it takes, and what to check before saying it is done.
---

# Building the artifacts after a feature lands

`make.sh` is one line, `docker build --output artifacts .`: the gate (restore, compile, every
test) and then the self-contained publishes for the default runtimes (`ARG RIDS` in the
Dockerfile: win-x64, osx-arm64, linux-x64) into `artifacts/<rid>/`. The user wants this run
unprompted whenever a feature lands on `main`, so the build of what landed is on disk under
`artifacts/` when they come back. `artifacts/` is ignored by git and by the Docker context.

## Run it from a copy, not from the repo

The repo lives in ownCloud, and buildkit refuses the folder's placeholder files (`ERROR:
invalid file request <path>`, naming a file that is not reliably the culprit). Copy the
tracked tree out first, which hydrates every file on read, and point `--output` at the main
checkout's `artifacts/`, which is where the user looks, whether the session is in a worktree
or not:

```bash
MAIN="$(git rev-parse --path-format=absolute --git-common-dir)/.."
TMP="$(mktemp -d)"
git ls-files -z -co --exclude-standard | tar --null -cf - -T - | (cd "$TMP" && tar -xf -)
cd "$TMP" && docker build --output "$MAIN/artifacts" . > "$TMP/make.log" 2>&1; echo "exit $?"
```

Run it from the worktree after `main` has been fast-forwarded, so the copy is the commit
that landed. Run it in the background: the gate alone is three to four minutes and the
publishes add a few more, and nothing else in the session waits on it. One build at a time;
a second `docker build` beside it fights for the same cores and the same cache.

## Read the whole log, then filter

Capture the output to a file and grep it afterwards, never in the pipe: a `tail` or a narrow
grep drops the per-test `failed <name>` lines and the run has to be repeated to learn what
broke. A gate failure means the commit on `main` does not pass the suite in a clean tree,
which is news the user needs in the first sentence, with the failing test's name.

## Say what is there

When it finishes, say which runtimes are under `artifacts/`, that they are the build of the
commit named, and nothing more. Do not launch the built app: `running-the-app.md` still
applies, and the artifacts are the user's to run.
