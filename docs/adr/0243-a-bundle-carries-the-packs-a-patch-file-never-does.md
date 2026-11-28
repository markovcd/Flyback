# ADR-0243: A bundle carries the packs; a patch file never does

**Status:** Accepted · 2026-11-26 · *user-directed* · follows
[0242](0242-a-pack-module-is-one-module-expanded-before-it-is-compiled.md)

## Context

A patch using a pack's module has to open where the pack is not installed. Copying the
pack's text into every `.fbks` and `.fbk` would do it, and was weighed: the user turned
it down, because it clutters the text a person reads and edits.

A patch already names its samples and pictures rather than carrying them, and a bundle is
the file that carries them (ADR-0052, ADR-0059).

## Decision

**A `.fbks` or `.fbk` holds no pack text.** A module's type id names its pack. The pack's
version rides in the provider name a `.fbk` already keeps (`ada.echoes 1.2.0`), so the
layout does not change and a patch says which version it was made with.

**Installed packs are the `.fbks` files in a packs folder**, `packs` in the data folder on
a desktop and `Packs` in the app's own folder on a phone, loaded with the plugins. A pack
with a mistake adds nothing and is listed with the plugins' problems, by file and line.

**A bundle carries each pack its patch uses**, whole, as `packs/<id>.fbks`. Reading one
adds the packs that are not installed to the catalog the patch is read against; an
installed pack stands over the bundle's copy.

**Opening a pack file installs it.** It holds modules, not a patch, so the editor checks it
and writes it into the packs folder as `<id>.fbks`, over any earlier version, rather than opening
an empty canvas; `flyback-cli packs install` does the same from a script.

**The preset site shares packs as it shares plugins.** A `.fbks` sent to `/api/v1/packs`
waits for Validate, which reads it against the modules that ship in the engine, and then
for the admin to publish it, since a pack's id is whoever sent it first and a newer
version replaces an installed one. The shelf lists each pack once, at its newest
published version. `flyback-cli packs get <id>` installs that version, downloaded only from the
site's own host and held to the hash the site lists; the plugins window lists them below the
site's plugins, under the same search, once the site shares any.

## Consequences

- A shared patch opens anywhere, offline; a loose one without its pack opens as one
  needing a missing plugin does, saying which pack and which version.
- An older Flyback ignores the `packs/` entries and reports the pack as missing.
- The editor's catalog changes while it runs: opening a bundle that brings a pack hands
  the catalog it was read against to `PluginCatalog.Take`, which installs it and raises
  `ModulesChanged`, and the module list is built again. What reads the modules asks the
  catalog each time rather than keeping the list it started with. The packs folder is read
  again when it has changed, before the module list opens, a file opens or a program
  driving the editor calls; a pack a bundle brought stays.
