---
name: nuget-packages
description: Use before starting a feature, refactor or anything else that will touch a lot of files - checking whether the solution's packages and the .NET SDK itself are current, what an upgrade costs, and when to take it on the spot versus hand it off.
---

# Packages and the SDK

## Check them before starting anything big

Before building a feature, a refactor or anything else that will touch a lot of
files, find out whether the solution's packages are current:

```bash
for p in $(find src tests -name "*.csproj"); do dotnet list "$p" package --outdated; done
```

Per project, because `dotnet list Flyback.slnx package --outdated` reports
nothing at all for a `.slnx`. Never `--include-prerelease`.

**Why:** building on a version that is about to move means writing the code
twice, and the upgrade's own breakages then arrive mixed into a diff that is
about something else. Finding out costs one command.

## Cheap upgrades are taken on the spot

An upgrade is cheap when the solution builds and every test passes after
changing the version numbers, or after a change a compiler error walks you
straight to. Take it, in a commit of its own, before the real work starts.

An upgrade is expensive when it wants a decision rather than a fix:

- a license or a fee (Verify 33 wants one of its maintenance-fee exemptions
  claimed, which is the user's declaration to make, not yours)
- a package that has to be vendored, replaced or dropped, because nothing
  downstream of it has been rebuilt yet
- a shipped behavior that would change, rather than a call that moved
- a breakage in something no test covers, so the port cannot be checked

Say what it costs and what it buys, recommend one, and let the user choose.
Then do the work they picked before the feature, not alongside it.

## .NET itself, the same way

Check the runtime in the same breath as the packages, because the answer changes
what a big piece of work is written against:

```bash
dotnet --version
curl -s https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json
```

The index gives `latest-sdk` and `support-phase` per channel. `go-live` is a
release candidate and still a prerelease, so it is not a version to move to.

Two places name the version, and a retarget changes both: `TargetFramework` in
Directory.Build.props, and `ARG SDK` in the Dockerfile. `global.json` has no
`sdk` section on purpose — it is there for the test runner — so nothing pins the
machine's SDK and nothing has to be edited to take a patch.

**Cheap, so just do it:** a new major that is already released *and* already
installed. Retarget, build the solution, run every test. Passing is the whole
test; that and the Dockerfile line are usually all of it.

**Not yours to do:** installing an SDK. That is a download onto the user's
machine, possibly needing elevation, and it is not part of the task they asked
for. Being a patch behind — 10.0.400 against 10.0.401 — changes nothing in the
repository. Say it in one line and carry on.

**Expensive, so hand it off:** a major that would need work rather than a
retarget — an analyzer wave the build turns into errors, a runtime behavior that
moved, a dependency with no build for the new target, or the release workflow
and Dockerfile needing more than their version line. Do not start it in the
middle of a feature. Say what it would take, and offer to spin a session of its
own for it, which is what the background-task chip is for. Then get on with the
work in hand against the version that is here now.

## The upgrade is its own commit

Never in the same commit as the feature it was cleared out of the way for. A
version bump that turns out to be the thing that broke something has to be
revertable on its own, and a feature diff with a package migration folded into
it is not reviewable. A retarget is the same: its own commit, ahead of the work.
