# ADR-0141: The preset site starts with a plugin its build packs and the release key signs

**Status:** Accepted · 2026-09-23 · *user-directed* · builds on
[0138](0138-the-preset-site-starts-with-presets-kept-as-files.md) for what a
default is, [0132](0132-a-plugin-package-says-what-it-is-and-installs-only-when-asked.md)
for what a package is, and [0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md)
for what publishing is

## Context

Every module plugin ships in the box, and a plugin reaches the site only by
being uploaded and published by hand. Figures ([0142](0142-figures-is-three-modules-that-are-each-a-picture-and-a-sound.md))
is the first plugin the site is meant to own the way it owns Tranquility: not in
the box, listed from the start, installed from the plugins window.

A package the editor will install has to be signed, and the site has no key. A
key of its own would be a second identity for the same author, and losing the
site's `data/` would lose it, after which no installed Figures would take an
update. The repository already has a signing key with a public half every copy
of Flyback carries: the release key.

## Decision

**A default `.fbkp` is seeded the way a default patch is.** `Defaults.Seed`
reads every file in the folder; a package goes to `PluginSubmissions.Read`,
which refuses what the editor would, and to `PluginStore.Seed`, which adds it
published, once, keyed by its file name in a `plugin_defaults` table. The same
file again changes nothing; a changed file replaces the stored package and
everything it says about itself under the same id, so downloads, ratings and
reports survive; one the admin deleted stays deleted. A file that is neither a
patch nor an installable package stops the site, as a bad setting does.

**The package is made by the site's image build, signed with the release key
handed in as a Docker build secret.** The site's Dockerfile runs
`flyback-cli pack-plugin` on the Figures project with the `release-key` secret,
which it requires, and writes `Defaults/Figures.fbkp` before publishing the site.
The key is in no layer. A rebuilt image carries a fresh signature and so a new
file, which replaces the stored one; the version decides whether the plugins
window offers anything.

**A plugin built beside the site stands in for a package nobody shipped.** The
site's build lays Figures out under `plugins/Figures/`, in Debug and Release
alike and never in its publish, and at every start the site packs any plugin
build it finds there as a build for any system and seeds it like a package, so
a run from the source lists what was just built. A plugin a shipped package
already covers is left to the package, so the image, which has the signed
`.fbkp` and no `plugins/` folder, never packs anything.

**Every key is `RELEASE_SIGNING_KEY`.** The Release workflow reads its secret
into that variable, and everything else reads the same variable: the site's
image build, `deploy.sh`, and a run of the site that packs a build. On a
developer's machine it holds a local test key, which `release-key.sh` and a
Release run of the site make and keep in the user environment where there is
none. A Debug build checks no keys at any stage, so a Debug site packs a build
unsigned when the variable is empty.

**A local build trusts the local key.** The app embeds the public key it takes
updates from. Built on a developer's machine, it embeds the public half of
`RELEASE_SIGNING_KEY`, which `ReleaseKey.targets` derives into `obj/` (and makes
the key where there is none); `release.sh` and `coverage.sh` hand a Docker build
the same half as `RELEASE_PUBLIC_KEY`. On GitHub and in any container without that
argument, the committed `release-key.pem` is embedded, and `release.sh` checks
the published signature against it. A local release therefore installs over
local builds, and nothing built locally installs a local release over a real one.

**The release is `release.sh`, and it runs off GitHub too.** It checks the key
and the changelog, then builds the root Dockerfile's `release` stage: the gate,
the publishes, Figures packed at the release's version, a zip per platform (a
folder to run, off GitHub) and a signed `SHA256SUMS`, into `dist/`. The workflow
runs it and publishes `dist/`. Run elsewhere it signs with the local test key
and publishes nothing, and it is the local build of anything on `main`. Only
GitHub stops at a key that does not pair with the committed public key or at a
missing changelog heading. A Figures installed from a release and one installed
from the site are one plugin to the editor's update rule.

**The plugin is not in the box.** It is not in the app's plugin list, so a
crash in it counts as a stranger's, and the test projects load it as an
install would: from a plugins folder, and packed as a package.

## Consequences

- The release key now signs two things: the checksum list and the Figures
  package. Rotating it means a new Figures assembly name for anybody who
  installed the old one, the same as any lost plugin key.
- A default plugin is migrated like a default patch: the change that breaks it
  rebuilds it, which the build does on its own.
- A package signed with a local test key is a different plugin to an editor that
  installed one signed with the release key, which is the point: nothing local
  passes for a release.
- There is no Gherkin scenario: the specs project reaches the engine, and this
  is the site. `PluginDefaultsTests` states the requirements.
