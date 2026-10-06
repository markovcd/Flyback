# ADR-0177: The .NET site is gone

**Status:** Accepted · 2026-10-06 · *user-directed* · supersedes
[0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md) where it
names the container, the NAS, the tunnel and the shared media folder, and the same
words in [0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md),
[0136](0136-a-letter-goes-to-the-author-through-the-preset-site.md),
[0138](0138-the-preset-site-starts-with-presets-kept-as-files.md) and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)

## Context

[0175](0175-the-preset-site-is-a-worker-and-github-reads-what-is-submitted.md) moved
the preset site to a Worker, and it has served `flybackmodular.app` since
2026-10-06 ([0176](0176-the-preset-site-lives-at-flybackmodular-app-alone.md)). The
old site held no data worth moving, so nothing reads the ASP.NET project any more.

## Decision

**`Flyback.Server`, its tests, its image build, its compose file and its publish script
are deleted, and so is `flyback-site export-site`, which only read the old database.**
What the Worker serves from that project lives where the Worker builds it: the preset
site's own pages sit in `site/` with the website's, the default presets are in
`worker/defaults/`, and the plugins the site starts with are listed in
`worker/site-plugins.proj`.

## Consequences

- Compression, caching headers and the media folder are the Worker's and Cloudflare's.
  The specs that started the .NET site read `site/` and the viewer's page from the
  repository instead; the Worker's own tests hold the caching rules.
- A rule the old site's tests held and the Worker's do not is a gap to write a Worker
  test for, not a reason to keep a project.
- Nothing runs the site on a developer's machine but `worker/dev.sh`.
