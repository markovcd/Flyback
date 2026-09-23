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
`flyback-cli pack-plugin` on the Figures project when the `plugin-key` secret
is mounted, and writes `Defaults/Figures.fbkp` before publishing the site. The
key is in no layer. Without the secret the image builds and the site starts
without Figures, for a developer; `deploy.sh` insists on `PLUGIN_KEY`. A
rebuilt image carries a fresh signature and so a new file, which replaces the
stored one; the version decides whether the plugins window offers anything.

**A plugin built beside the site stands in for a package nobody shipped.** A
Debug build of the site lays Figures out under `plugins/Figures/`, and at
start the site packs any plugin build it finds there as a build for any
system, signs it with a P-256 key it makes once and keeps beside its database
(`Presets:PluginKey` names another), and seeds it like a package. A plugin a
shipped package already covers is left to the package, so the image, which is
a Release publish with the signed `.fbkp` and no `plugins/` folder, never
touches the key path. A run from Rider therefore lists Figures at once, signed
by a key that survives restarts, so the editor takes its rebuilds as updates.

**The Release workflow packs the same package with the same key** in a
`figures` stage of the root Dockerfile, lists it in `SHA256SUMS` and attaches
it to the release. A Figures installed from a release and one installed from
the site are one plugin to the editor's update rule.

**The plugin is not in the box.** It is not in the app's plugin list, so a
crash in it counts as a stranger's, and the test projects load it as an
install would: from a plugins folder, and packed as a package.

## Consequences

- The release key now signs two things: the checksum list and the Figures
  package. Rotating it means a new Figures assembly name for anybody who
  installed the old one, the same as any lost plugin key.
- A default plugin is migrated like a default patch: the change that breaks it
  rebuilds it, which the build does on its own.
- There is no Gherkin scenario: the specs project reaches the engine, and this
  is the site. `PluginDefaultsTests` states the requirements.
