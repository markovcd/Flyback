# ADR-0131: Shared presets live on a site that only reads its media

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) for the file that is
shared and [0060](0060-a-bundle-is-a-patch-and-what-it-names.md) for the bundle

## Context

People want to share the patches they make. The GitHub Pages site is static,
so it cannot take a submission. The user's NAS can run a container but has no
power to spare, and a render of a picture, a loop and a track is minutes of
CPU. A more powerful machine is on the same network. Submissions come from the
website first and from Flyback later, and go up without review.

## Decision

**The site is an ASP.NET project, `Flyback.Server`, in a container on
the NAS.** It references Flyback.Engine only, to read a submission with the
same `PatchIO` the app opens files with. A file that is not a patch is refused.
A patch with modules this build does not know is taken, because plugin presets
are presets. Its pages link `site/assets` in at build time, so the two sites
share one stylesheet.

**A preset's name, author, description and tags come from the patch.** The
format carries all three besides the name, and the name defaults to the file's.
There is no second place to describe a preset, so what the site says and what
the app shows cannot disagree.

**Metadata and the preset file go in SQLite. Media goes in a folder.** An MP3
in a blob would bloat the database and could not be streamed with seeking.

**`flyback-cli render-presets` runs on the other machine and writes into the
site's media folder over a share.** It finds work through the site's public
read API (`?pending=true`) and renders in-process with the same code as
`flyback-cli render`, plugins included, so the render machine needs only a
Flyback install and ffmpeg. A patch that does not open whole, one short of a
plugin there, is refused rather than rendered without its missing modules.
Each file is written under a temporary name and renamed, `{id}.done` goes last,
and `{id}.failed` holds why a render could not be made. The site mounts the
folder read-only.

**The site has no API that writes media.** The only public write is a
submission, which is rate-limited per address. The API is versioned (`/api/v1`)
so the app can submit through the same endpoint later.

**One admin, named in the container's configuration, moderates after the
fact.** `Site__Admin__User` and `Site__Admin__Password` in the compose
file are the whole account; there is no user table, and admin mode is off while
either is blank. Signing in sets a cookie whose keys sit beside the database.
The admin renames, unpublishes and deletes presets on the same pages everyone
sees. An unpublished preset stays in the database but is gone from the shelf,
its page, its download and the render queue for everyone else.

## Consequences

- The render machine needs the share mounted. It cannot render for a site it
  cannot reach as a file system.
- A preset whose plugin is missing on the render machine is marked failed
  rather than retried forever. Deleting the marker renders it again.
- Deleting a preset leaves its media behind, since the site cannot write the
  folder. The files are orphans until removed by hand.
- The Pages top bar links to the site. The site's pages copy the Pages header,
  so a change to one header is a change to both.

## Amendment, 2026-09-24: the site serves the whole website

The site links in all of `site/`, not only `site/assets`, and its own pages
are the ones that need it: the presets and plugins, a preset or plugin, the
two submit pages and the admin's. So its root is the overview, and one host
serves everything. Its pages take names `site/` does not use (`presets.html`,
`shared-plugins.html`), and every link between the two sets is relative.

On Pages those neighbors are elsewhere. `pages.yml` rewrites each link to a
page in `src/Flyback.Server/wwwroot` to the address in the `PRESETS_URL`
repository variable, and leaves them relative while it is unset.
