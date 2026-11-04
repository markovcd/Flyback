# ADR-0222: The website is built by Hugo from one layout

**Status:** Accepted · 2026-11-04 · *user-directed*

## Context

Every page in `site/` was a whole HTML file: its own `<head>`, toolbar, footer and
`theme-color`, with the site's colors copied by hand from the Graphite scheme into
`site.css`. A color-scheme change missed the tutorials page, and the footers had
drifted into four versions. A script that stamped one toolbar and footer into every
page, with a spec to catch a page that differed, stopped the drift but left a copy in
every file. Arek asked for the duplication to go, and for a tool that already solves
it rather than a build script of our own.

## Decision

- **Hugo builds the site.** One pinned binary (0.169.0, checked against its release
  checksum), no npm dependencies. Eleventy was weighed and passed over for its
  33 direct dependencies; PostHTML's layout plugin has not had a release since 2022.
- **One layout holds what every page shares.** `site/layouts/_default/baseof.html` has
  the `<head>`, the toolbar and the footer; a page in `site/content/` holds its own
  body and a front-matter header (title, description, section). The toolbar marks the
  page's section current.
- **Colors are written once, in the app.** `graphite.json` is mounted as Hugo data;
  `site.css`'s color block and every page's `theme-color` are filled from it.
- **Files that are not pages are copied as they are**, from `site/static/`.
- **`worker/build-assets.sh` runs Hugo into `worker/public`**, and the specs read the
  built site, so what is checked is what is served.

## Consequences

- A file in `site/` is no longer a page a browser can open; `hugo server --source site`
  previews the site with live reload.
- The gate image and the Worker's build carry Hugo, pinned like everything else
  (the `pipeline` skill).
- `scripts/site-frame.mjs`, `site/partials/` and the spec that guarded them are gone.
