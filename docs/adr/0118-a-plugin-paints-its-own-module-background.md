# ADR-0118: A plugin paints its own module's background

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

[0116](0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md) made a
module's category the whole of how it is drawn: the accent washes the body, the
header takes the band, and a category the engine does not name draws grey and no
mark at all. Every plugin category is one of those. A plugin's modules are
therefore the only grey blocks on the canvas, and three plugins installed
together are one undifferentiated grey.

A plugin cannot fix that from where it stands. `NodeDef` says what a module
computes and what its sockets are called, and nothing about what it looks like;
the shell owns the painting, and by
[0055](0055-a-plugins-extra-declares-its-editor.md) that is
deliberate — a plugin ships no control, so no plugin binary is pinned to the
Avalonia a given build shipped.

The question is how much of the block to hand over. All of it is the wrong
answer: a canvas of modules that each laid themselves out differently stops
being a patch, and the reason a Flyback patch can be read at a glance is that
every block is the same block.

## Decision

**A plugin paints the background of its module and nothing else.** `NodeDef.Skin`
is a `ModuleSkin` or null. The shape, the corner radius, the header, where the
title sits, the socket rows and the description are not a plugin's to move.

**There are three backgrounds**, a closed hierarchy the shell paints by asking
which it is. Colors are a `Swatch(red, green, blue)`, because Core draws nothing
and names no toolkit.

**A `Palette` stands exactly where a category accent would.** One `Accent`, an
optional `Floor` for the wash to fall to, and an optional `Glyph` as SVG path
data on the same twenty-four unit box `ModuleGlyphs` draws the engine's own
marks on. Everything is derived by the arithmetic already in `NodeSkin`, so a
skinned module is a Flyback module that happens to be a color the engine does
not ship. Path data that will not parse draws nothing, which is what a mark does
when it is not there ([0025](0025-platform-io-behind-loadable-plugins.md)).

**A `Grain` is a `Palette` with a texture cut across it** — `Hatched`, `Milled`
or `Beaded`, tiled in graph units so the texture holds its pitch at any zoom. It
subtypes `Palette` rather than sitting beside one, so every color derivation
already written applies unchanged and the cut is a second channel over the
first. The cut is named rather than drawn by the plugin, because a texture is
the one thing a background wants that must be the same at every size: a named
cut is scale-free where a picture of a hatch is not.

**An `Artwork` is a picture** — SVG, PNG or GIF — scaled to cover the body and
clipped to it. Bytes rather than a path, so a skin needs nothing deployed beside
the assembly and nothing read off disk while the canvas paints. It runs behind
the header band rather than stopping below it, because it *is* the background
and the band is the one part of the block that is not; the band's relief is
still drawn, so the title bar reads. An animated GIF runs unless `Animate` is off, and the
canvas asks for another frame only while a module that is animating was actually
drawn — a patch with none repaints when the patch changes, as it always has.

**Text is white unless the module asks otherwise.** `ContrastText` is opt-in on
all three. White is what the rest of the canvas is written in, and a plugin that
picked a dark color should not be the one module whose title is a different
color from every other.

**Where it is asked for, every line of text is derived from the background that
line covers**, by one rule with no table in it. `Colors.Contrast` takes the
inverse of the background and drives it toward whichever pole the background is
not until the two are half the luminance range apart. The inverse alone is the
obvious rule and it fails silently in the middle — a mid-grey inverts to itself,
and anything near one inverts to something barely off it. The drive is solved
for rather than stepped: luminance is linear in each channel, so the amount that
buys exactly the separation wanted is an equation, and text over a gradient
stays continuous instead of breaking where a threshold would have been.

**The direction is decided once for a surface, not per stop.** Dark ink at one
end of a word and light at the other is worse than either, and both answers are
equally readable. A painted module has two surfaces — a pale accent makes a pale
header band over a body that is nearly all node grey — so the title and the
labels are allowed to differ from each other and never from themselves.

**A picture answers the same question through its bands.** An artwork is read
once at decode time into thirty-two horizontal averages, with transparency
counted as the node grey behind it, and the ink rule runs against those in place
of a gradient's stops.

## Consequences

**The contrast rule is arithmetic, and is tested as arithmetic.** 5,832 points
across the color cube, each asserting the ink and the background are half the
range apart. That is the whole promise, and it is the kind that fails on exactly
the one background nobody happened to pick.

**One direction per surface is a real limit, and it is the right one.** Where a
line of text sits on a patch of the background far from that surface's mean —
the corner of a busy picture — its ink can be closer than the promised half.
Making it per-stop would fix that case and tear a word in half in every other.

**`Palette` is unsealed.** `Grain` subtypes it, which is what lets `is
ModuleSkin.Palette` keep catching both wherever only the colors matter. The
hierarchy is still closed to the outside by a `private protected` constructor: a
kind from another assembly would be a background nothing knows how to paint.

**The shell took an SVG dependency.** `Svg.Controls.Skia.Avalonia`, MIT, by the
author of AvaloniaEdit's port. Avalonia decodes PNG on its own and the frames of
a GIF through the Skia it already carries; SVG it does not read at all, and
vector artwork drawn at whatever zoom the canvas is at is the case worth having.
[0019](0019-no-third-party-dependencies-in-the-engine.md)'s rule is the
engine's, and Core still answers to it. The package is named for Avalonia 12 —
`Avalonia.Svg.Skia` is the same library's Avalonia 11 line and stops there.

**Decoding is bounded and cached.** A picture is read once per skin, the failure
too, so bytes that are not a picture are not decoded again every frame. Frames
are capped at 120 and the longest side at 512, because a module is a couple of
hundred pixels of canvas and every frame is held decoded.

**A skin is per module, not per plugin.** Four unsteered agents asked to make a
plugin's modules recognizable all reached for one livery hung off
`ModuleProvider`, and that is the conventional shape — but it is a rack badge,
and what was wanted is a module author painting a module. A plugin that wants
one look gives its modules one skin, which costs a shared field.

**A skinned module opts out of being read as its category.** That is the trade
and it is the author's to make: 0116's whole point is that the accent is how a
patch is read at a glance, and a plugin choosing artwork is choosing something
else. `Palette` and `Grain` exist so that an author who wants to stay in the
family can.
