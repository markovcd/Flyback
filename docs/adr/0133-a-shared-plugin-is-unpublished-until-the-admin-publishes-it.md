# ADR-0133: A shared plugin is unpublished until the admin publishes it

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md) for the
site and its admin, and [0132](0132-a-plugin-package-says-what-it-is-and-installs-only-when-asked.md)
for the package

## Context

Plugins are shared as `.fbkp` packages, and the preset site already takes files
people submit. A preset is data, so it goes up at once and is moderated after the
fact. A plugin is code that runs with everything its user can do, and a site that
lists one is vouching for it a little, whatever it says.

Flyback will later download plugins from the site itself, so the site's API is
the one the editor will read.

## Decision

**The preset site takes plugin packages too**, beside the presets: a `plugins`
table in the same SQLite file, the package kept whole as a blob, under
`/api/v1/plugins` and on pages of their own.

**A package arrives unpublished.** Until the admin publishes it, it is not
listed, its page is not found and its file is not served, to anyone but the
admin. The submitter is answered 202 with what the site read, not a link.

**What is listed is what the editor's dialog shows, read the same way.** The
server reads a submission with `PluginPackage`, the class the editor opens a
package with, and so runs none of it and refuses whatever the editor would. The
name, version, author, description, tags, preview, what it adds, what it reaches,
the systems it has builds for, the contract versions it was built against and the
SHA-256 all come from the package. There is no form field for any of them. The
preview is served from `/api/v1/plugins/{id}/preview` with its own image type, and
a tag filters the listing as it does the presets'. The file is served
as `<assembly>.fbkp`, whatever it was uploaded as. The same package twice is
refused.

**The API is shaped for the editor to download from.** The listing filters by
`platform` (`win`, `osx`, `linux`, counting a build for `any`), and each plugin
carries its `assembly`, `version`, `contract`, `modules` and `sha256`. `module`
finds the plugin declaring a type id, which is how a patch naming a module nobody
has installed will find its plugin. The editor's plugins window lists what is
installed beside what the site offers for this system, narrowing both with the
site's own search: every word somewhere in the name, author, description,
assembly, tags or modules, and a tag whole. It checks the downloaded bytes
against that hash and then opens them with the same dialog as a file on disk:
the listing is for choosing, and the dialog stays the only place a plugin is
agreed to. The window asks the site running locally, on `localhost:8790`, until
the site has a public address. `count=false` fetches without counting a
download, as for presets.

**A package may be 64 MB**, half what the editor accepts, since the site holds
every one in its database. It is the only endpoint allowed past the site's 20 MB
request limit.

## Consequences

- Publishing is not an audit. The pages say so, and send the reader to the
  dialog.
- A package is tied to its key, not to a person. An unsigned package is refused as
  the editor refuses it, and once a plugin is published its assembly name is its
  key's: a submission of that assembly signed by another is refused, and so is
  publishing one. Each plugin carries its key's fingerprint as `signer`.
- An update is a new submission, reviewed again. The listing has no notion of
  one package replacing another.
- The specs project cannot host the site, so the requirement lives in the
  server tests.
