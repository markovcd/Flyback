# Packages

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

- a licence or a fee (Verify 33 wants one of its maintenance-fee exemptions
  claimed, which is the user's declaration to make, not yours)
- a package that has to be vendored, replaced or dropped, because nothing
  downstream of it has been rebuilt yet
- a shipped behavior that would change, rather than a call that moved
- a breakage in something no test covers, so the port cannot be checked

Say what it costs and what it buys, recommend one, and let the user choose.
Then do the work they picked before the feature, not alongside it.

## The upgrade is its own commit

Never in the same commit as the feature it was cleared out of the way for. A
version bump that turns out to be the thing that broke something has to be
revertable on its own, and a feature diff with a package migration folded into
it is not reviewable.
