# ADR-0175: The preset site is a Worker, and GitHub reads what is submitted

**Status:** Accepted · 2026-10-05 · *user-directed* · builds on
[0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md),
[0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md),
[0138](0138-the-preset-site-starts-with-presets-kept-as-files.md) and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md);
implemented in `worker/` and `src/Flyback.Site`

## Context

The preset site runs in a container on the author's NAS behind a Cloudflare
Tunnel, and its media is rendered on another PC that writes into a share. The
author wants no machine of theirs serving or storing anything for it. Cloudflare
can hold the state and answer every request, but a Worker runs JavaScript and
WebAssembly in 128 MB, and a submission is checked by the app's own readers:
`PatchIO`, `PatchBundle`, `PluginPackage` and the module catalog that decides what
a browser can open.

## Decision

**The site is a Worker in TypeScript (`worker/`), with rows in D1 and every file in
R2.** It serves the same `/api/v1` with the same JSON, and the pages, the web viewer
and the web editor as static assets on the same hostname. Rate limits are a D1 table,
since Cloudflare's own binding counts in 10- or 60-second periods, and a visitor is
`CF-Connecting-IP` with an IPv6 address counted as its /64. Search matches a folded
column written with the row, since D1 has no custom functions.

**The Worker checks only what is cheap and certain**: the form, the size and the
first bytes. A submission answers 202 and waits `unchecked`, listed nowhere and
served to nobody but whoever holds its id, who can read why it was refused.

**The app's readers run on GitHub's machines, in `flyback-site`.** The Worker starts
the Validate workflow on each submission, a schedule is the net, and
`flyback-site validate-submissions` reads each file with the same code the app opens
files with, never running a package, and sends the verdict back. Rewriting the
readers in TypeScript would be a second `PatchIO` to drift from the first; loading
the engine as WebAssembly in the Worker would be a runtime nobody tested, under a
memory ceiling.

`flyback-site` is a program of its own rather than commands of `flyback-cli`: what
a browser lacks is checked against the plugins the web pages link, which it
references as the .NET site did, and `flyback-cli` ships in the app's folder, where
those assemblies would be loaded twice.

**Admin is Cloudflare Access, and the Worker checks Access's signed token itself**
on every route under `/api/v1/admin/`, from the header Access adds or the cookie it
leaves. A service token is how Validate, the Worker workflow and `render-presets` on
the author's PC reach those routes. Every admin-only route moved under that prefix,
because Access gates by path and the old `PATCH /presets/{id}` shared one with the
public `GET`.

**The author's PC still renders, and uploads what it makes** through
`PUT /api/v1/admin/presets/{id}/media/{name}`, `done` last. It listens for nothing.

**The defaults are files sent by the Worker workflow** after each deploy, each with
its check, and kept to the file as 0138 and 0141 say.

## Consequences

- A preset appears a few minutes after it is sent, not at once.
- The token that starts Validate can start any workflow in the repository; without
  it the schedule picks submissions up, ten minutes later at most.
- A static asset is 25 MiB at most, and the ahead-of-time web editor's runtime is
  larger. A fingerprinted framework file past the limit is put in R2 by the Worker
  workflow and served from there on the same path; anything else that large fails
  `worker/build-assets.sh`.
- The gate builds a Node stage and runs the Worker's tests in workerd. The npm
  packages are development dependencies only, locked in `package-lock.json`.
- Until the move is finished the .NET site keeps serving, answers the new admin
  paths too, and `render-presets --media` keeps writing its share. At the move,
  `flyback-site export-site` carries its rows and files over with their ids, and
  this ADR replaces what 0131, 0133, 0136, 0138 and 0141 say of the NAS, the share
  and the container.
