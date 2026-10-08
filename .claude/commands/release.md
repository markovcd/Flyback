---
description: Check the changelog and the plugin contract, land the release commit on main, and fire the Release workflow.
---

# Release

Publish a release. The work is the checking; the publishing is one dispatch at
the end of it.

`$ARGUMENTS` says which number, and nothing else:

| Argument | 0.4.2 becomes |
| --- | --- |
| *(nothing)* | 0.5.0 |
| `major` | 1.0.0 |
| `patch`, `hotfix` | 0.4.3 |
| `1.4.0` | 1.4.0 |

The word `check` anywhere in the arguments stops before the dispatch, having
done and reported every check. Everything up to that point is safe to run
whenever.

The base is the newest tag, never the changelog and never Directory.Build.props:

```bash
git tag --list "v[0-9]*.[0-9]*.[0-9]*" --sort=-v:refname | head -n1
```

## Refuse before moving anything

Each of these ends the run, says which one and why, and changes no file.

- **The tree is not clean, or holds work this session did not do.** A release is
  a commit of the whole repository; `git-workflow.md` applies at its hardest here.
- **`main` is not what would be released.** The workflow builds the tip of the
  branch it is dispatched on. Rebase and fast-forward first, as usual.
- **The tag exists**, or `HEAD` is already tagged.
- **`## Unreleased` is missing or has no bullets.** A release with nothing in it
  is a mistake.
- **The gate has not passed on this commit.** ADR-0120 put the Dockerfile gate on
  every push, so its verdict is known before the release build spends twenty
  minutes rediscovering it:

  ```bash
  gh run list --commit "$(git rev-parse HEAD)" --json workflowName,conclusion,status
  ```

  A red run refuses. So does **no run at all**, which is the easier one to walk
  past: a commit that was never pushed has nothing red about it, and asking
  whether CI is unhappy answers no. The gate having no opinion is not the gate
  being happy. Push and wait for it.

- **The Android build has not passed on this commit.** `android.yml` is outside
  the gate (ADR-0184) and runs on every push to `main`. Same rule as the gate: a
  red run refuses, and so does no run.

  ```bash
  gh run list --workflow=android.yml --commit "$(git rev-parse HEAD)" --json conclusion,status
  ```

- **A locked restore fails.** The gate restores with `--locked-mode`, so a release
  build dies at restore if `Directory.Packages.props` and the `packages.lock.json`
  files disagree — which a version bump is exactly the thing to cause:

  ```bash
  dotnet restore Flyback.slnx --locked-mode
  ```

  Where it fails, `dotnet restore Flyback.slnx --force-evaluate` regenerates the
  lock files, and they are committed with whatever moved the versions.

- **`gh auth status` is not signed in**, with rights to run a workflow.

## The changelog

The `changelog` skill has the rule; this is the release's reading of it.

Groom `## Unreleased` before renaming it:

- **One bullet per feature.** Two bullets about the same thing are one bullet.
  Cut clauses starting "which", "so that", "because".
- **Nothing a user would not notice.** Drop bullets that are only tests, ADRs,
  comments, the README or a refactor. No Internals section.
- **Group under `###` headings** the way 0.4.0 does — Modules, Presets, Viewer,
  Interface, Fixes — once there are enough bullets to need them.

Then rename the heading and count the run:

```
## 0.5.0 — 2026-09-22

103 commits since 0.4.0.
```

Em dash, `X.Y.Z`, today's date. The workflow greps for that heading and refuses
without it, and `ReleaseNotes.Of` reads the section out of the changelog built
into the binary to fill the What's-new window — a malformed heading ships a
release that cannot describe itself. Count with
`git rev-list --count v0.4.0..HEAD`.

Leave an empty `## Unreleased` above it for the next bullet.

## The plugin contract

`PluginContractVersion` in Directory.Build.props is the only version number
committed in this repository. `Version` stays `0.1.0-dev`; the release passes the
real one with `-p:Version=`. Core and Plugins both take the contract version as
their `AssemblyVersion` from that one property, so they cannot disagree — check
instead that neither csproj has grown a number of its own.

**Whatever HEAD says is noise.** An agent may have raised it in passing. Take the
value at the last tag and work out the new one from the surface itself. The one
exception: after v0.4.0 the contract was reset to 1.0.0 with its whole surface
shipped, so a release whose last tag is v0.4.0 starts from 1.0.0, not from what
that tag says.

```bash
git show v0.4.0:Directory.Build.props | grep PluginContractVersion
grep -c . src/Flyback.Core/PublicAPI.Unshipped.txt src/Flyback.Plugins/PublicAPI.Unshipped.txt
grep -F "*REMOVED*" src/Flyback.Core/PublicAPI.Unshipped.txt src/Flyback.Plugins/PublicAPI.Unshipped.txt
```

- A `*REMOVED*` line in either file → **major**, minor to 0.
- Otherwise a line beyond `#nullable enable` in either → **minor**.
- Otherwise it does not move. The patch number never moves.

This is independent of the release number. A `/release patch` that removed a
public member still bumps the contract major, and a `/release major` that changed
no surface leaves the contract where it is.

## Shipping the surface

A release moves what is unshipped into what is shipped, per project:

- A `*REMOVED*` line **deletes** its member's line from `PublicAPI.Shipped.txt`,
  and is not carried over.
- Every other line merges into `PublicAPI.Shipped.txt`, which stays sorted under
  its `#nullable enable`.
- `PublicAPI.Unshipped.txt` is left as `#nullable enable` alone.

Then prove it, which is one build — the analyzer is a compile-time check, and
Plugins references Core, so building Plugins runs both. Ten seconds:

```bash
dotnet build src/Flyback.Plugins -c Release -v q --nologo
```

RS0016 means a line was dropped; RS0017 means one was carried over that should
not have been.

## The install script

`install.sh` is what the website tells people to pipe into bash, and nothing else
in the repository tests it. Run it, both sides of the release.

**Before**, against the release that already exists — this proves the script
works without needing the new one to be published. `--no-links` writes only the
copy, with no command links, menu entry, Start menu shortcut or PATH change;
without it a scratch `FLYBACK_DIR` still replaces the user's own links and entry,
and `--uninstall` then removes them:

```bash
FLYBACK_VERSION=0.4.0 FLYBACK_DIR="$SCRATCH/flyback-install" bash install.sh --no-links
FLYBACK_DIR="$SCRATCH/flyback-install" bash install.sh --uninstall
```

That is the download, the SHA256SUMS, the signature against the key committed in
the script, the unzip and the removal, for real. Git Bash reports `MINGW*` and
takes the `win` branch. `bash -n install.sh` costs nothing, and `shellcheck` if
it is on the machine.

The links and entry are left untried by that, so on Linux and macOS run the
default install once under a throwaway home, which keeps `~/.local` yours:

```bash
HOME="$SCRATCH/home" bash install.sh
HOME="$SCRATCH/home" bash install.sh --uninstall
```

Never run the default install on the user's own account to test it, and on
Windows, where the shortcut and PATH cannot be redirected, not at all.

**After** the workflow finishes, the same run with no `FLYBACK_VERSION`. That one
is the point: it says the artifacts just published are installable.

## Landing it

One commit, subject as a declarative sentence — `Flyback 0.5.0 is released` —
carrying the changelog, both projects' API files and the contract version if it
moved. Land it on `main` and push; the workflow builds `github.sha` and refuses
a changelog without the heading, so nothing is dispatched until the commit is up.

Do not create the tag. The workflow does, and a tag already there ends the run.

When `release.sh`, `release.yml` or the Dockerfile's `release` stage changed since
the last release, run `./scripts/release.sh <version>` here first. It is the workflow's
build, signed with the local test key into `dist/`, and it fails where the
workflow would fail, minus the key pairing.

## Firing it

```bash
gh workflow run release.yml --ref main -f version=0.5.0
gh run watch "$(gh run list --workflow=release.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
```

It restores, builds, runs the whole suite in Docker and publishes three
self-contained platforms, so it takes a while. Say so rather than polling in
silence; offer to watch it in the background.

The site needs nothing: it links `releases/latest`, and the platforms it claims
are the Dockerfile's default RIDs, unchanged by a release.

## Reporting

The number, what the contract did and why, what was cut from the changelog, both
install runs, and the release URL. If the workflow failed, the failing step's log
and nothing softer.
