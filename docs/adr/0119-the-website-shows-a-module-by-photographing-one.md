# ADR-0119: The website shows a module by photographing one

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

The site drew its own modules. `site/assets/site.js` held a small SVG renderer
that read a patch out of a `data-patch` attribute and painted a header band,
socket rows, wires and values from a table of the canvas's numbers; `.node` and
`.ports` in `site/assets/site.css` did the same job in CSS for the cards. Four
figures were drawn that way — the Plasma patch on the front page and three steps
of the tutorial.

Neither drawing is the canvas. They were a transcription of it made at one
moment, and the canvas has moved since:
[0116](0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md) gave a
module its category's wash and a glyph of its own,
[0117](0117-a-module-switched-off-is-a-wire.md) gave a switched-off module its
own look, and [0118](0118-a-plugin-paints-its-own-module-background.md) let a
plugin paint the background. None of that reached the site, and none of it ever
would have: every such change would have needed a second, hand-written version
of itself in JavaScript, noticed by somebody who remembered the site drew its
own.

`SkinShotTests` had already answered this for the plugin guide's pictures —
it draws real modules with the shell's own painting code and writes them out.

## Decision

**Nothing in `site/` draws a module, a socket or a wire.** The renderer, the
`data-patch` figures, `.node`, `.ports` and the `--port-*` colors are gone. A
module reaches the website only as a picture taken of one.

**`PatchShotTests` takes them**, beside `SkinShotTests` and in the same shape: a
real `NodeEditor`, `FrameAll`, a crop to the modules' own bounding box, and a
PNG per patch under `SHOT_DIR`. The patches are the shipped Plasma preset and
three short `.fbks` sources, so what is photographed is a patch the app would
open rather than a drawing of one.

**The cards are cards.** `.card` has a colored top edge and no module in it: a
flat panel with a heading. What was `.node` looked like a module because it was
easier to borrow the shape than to design one, and what it depicted — a feature
of the program, a track, a tier of plugin state — is not a module.

## Consequences

**A capture is retaken, not edited.** Set `SHOT_DIR`, run the test, convert the
PNGs to webp at quality 88, drop them in `site/assets/shots`. The `.claude/rules/website.md`
rule already covers stale screenshots and now covers these.

**The pictures are wide, and the page gives them the room.** Nine real modules
laid out left to right are 5.7:1, where the drawing could be packed into any
shape the author liked. The Plasma figure took the full width of the front
page's wrap, and every capture carries the `.zoom` link the full-window shots
already use, because a patch shrunk into a column is unreadable however it got
there.

**A module that changes appearance makes four pictures stale at once, and
nothing says so.** That is the trade: a stale photograph is visibly out of date
and one command fixes it, where a stale drawing was invisibly out of date and
needed the drawing code changed to match.
