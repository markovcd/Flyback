# ADR-0163: A build draws every preset's still once, for every program to show

**Status:** Accepted · 2026-09-29 · *user-directed* · builds on
[0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md)

## Context

The gallery drew each preset's still from its patch the first time its tile came into
sight, and kept it on disk under the build that drew it. A file of stills was ruled out
because it would have to be re-shot every time a patch changed.

In a page that drawing shares the one thread with the editor, and the presets page,
which is static on GitHub Pages, has no way to draw one. The preset site's shared presets
already arrive with a still drawn elsewhere.

## Decision

**`flyback-cli stills --out <folder>` draws every preset the build offers**, the way the
gallery does (`PresetStill`, a second and a half in), as a JPEG each, with `index.json`
listing every preset in the editor's order with its name, kind, heading, description,
author, tags and still, and the build that drew them.

**The pipeline runs it once per build**, through `stills.sh`, which lays the editor and
the command line out side by side so the shipped plugins load: the release lays the
stills beside each platform's programs, and the preset site's image and the Pages
workflow publish them at `/stills/`. The preset site's web viewer carries the site's
plugins and its stills are drawn with them (`CONFIGURATION="All plugins"`); Pages builds
the viewer with `-p:SitePlugins=false` and draws only what the desktop ships. The index
is the presets page's list of shipped presets.

**A program shows a still only when the index is its own build's.** The desktop reads
`stills/` beside itself and the web editor fetches `../stills/`; a saved preset, a
plugin installed later, or a build with no stills is drawn as before and kept on disk.

## Alternatives considered

**Keying each still by its patch.** A preset's modules are given new ids every time it is
built, so its file differs every time; the build's version is what the stills and the
programs share.

**Committing the stills.** They would have to be re-shot by hand, which is what ruled
files out in the first place.

## Consequences

- A build takes about fifteen seconds more to draw about sixty presets.
- A local build has no stills and draws its gallery, as it always did.
- A preset whose drawing changes shows the new still with the build that changed it.
