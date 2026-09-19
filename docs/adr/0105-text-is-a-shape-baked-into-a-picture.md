# ADR-0105: Text is a shape, baked into a picture

**Status:** Accepted · 2026-09-19 · *user-directed* · gives the text field
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) added several lines, and builds on
[0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md) and
[0059](0059-a-picture-comes-in-as-a-texture.md)

## Context

Nothing could put a word on the screen. A title, a caption or a lyric had to be
drawn by hand out of boxes, or brought in as a photograph of itself, and then it
could not be changed without leaving the program.

Two things stand in the way. A wire holds a number, never a string
([0007](0007-register-slots-with-scalar-broadcast.md)), and a program has no
branches or lists to index ([0005](0005-compile-to-a-flat-register-machine.md)),
so "show the third line" is not something a patch can compute. And the engine
may take nothing that rasterises a font
([0019](0019-no-third-party-dependencies-in-the-engine.md)), while a font read
off the machine would draw one patch differently on every machine it was opened
on.

## Decision

**Text is a module in the Picture plugin, and it is a shape.** Its output is the
signed distance to its letters, like every other form there
([0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md)). A Fill inks it,
a Combine melts it into a circle, and a Transform turns it, and none of them know
that it is text.

**The words are carried on the node, and a wire chooses where to read them.** At
compile time the lines are drawn into one picture, a band to a line, and read
through `SamplePicture` ([0059](0059-a-picture-comes-in-as-a-texture.md)), so both
backends draw it and the shader keeps it. The one place a program can look
something up by a number it computed is a texture. So `line` chooses a band by
where the picture is read: floored, and wrapped round past the last line. That is
a real index, where a sequencer builds one from a window per step. `reveal` types
the line out. The picture's second channel holds, for each texel, the share of
the line that must be showing before the letter nearest it appears. Nearest
rather than the letter whose cell the texel is in, so a hidden letter's edge
never shows in the gap beside the letter before it.

**The picture holds a distance, not ink.** It is eight bits a channel and
filtered, so ink stretched to a whole frame would blur. A distance filtered
between texels still has a sharp zero. Because the letters are pixels, the
distance is exact: a glyph is a union of squares, and a square's distance has a
formula. It is written to four font pixels either side of an edge. Beyond the
picture, a box around the ink takes over, so the black outside the texture never
reads as ink.

**The fonts are drawn in the repository, and chosen on the node.** Pixel is
printable ASCII, five pixels by nine: seven to the baseline and two for tails.
Tiny is three by five and has one case, drawing a small letter as its capital.
Each is kept in the source as the sheets it was drawn on. A character a font has
no glyph for is drawn as an empty box, so what was typed can be seen to be there.
The font is a choice field beside the lines, so the panel offers it as a list, and
a patch naming a font this build lacks is drawn in Pixel with a warning, keeping
its choice. 'size' is the height of a capital in either font, so changing the font
changes the letters and not how big they are.

**The words are the text field an Expression's formula already is
([0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md)), given several lines.** `ExtraField.Text`
gains `Multiline`, rather than Text getting a kind of extra with its own control.
So the inspector draws a taller box, where Enter starts a new line and Ctrl+Enter
keeps the text. An assistant sets it with `set_extra`, with `\n` between lines. Any
plugin can carry a caption as easily as a formula. The text is still kept as
typed, apart from one change: in a field of several lines, a line break is one
character, whoever typed it. The limit rises from 500 characters to 4,096, which
is enough for a full atlas.

**In the language, a line break inside text of several lines is written `|`.** A
string is one line and has no escapes, which is deliberate because a path is full
of backslashes. Text holding a `|` of its own has no spelling, so it is left out
of a printing, the way a string holding a quote already is. Text of one line,
such as a formula, is written as it always was.

**It is baked at both sinks.** Reading a texture is arithmetic to the program
that plays too, so a loop swept through a word is heard as its letters, the way
every other shape can be.

**A preset teaches it.** Captions pages four lines that describe the module, with
the clock choosing the line and typing it out.

## Consequences

The same text bakes to the same picture instance, and a handful of texts are
remembered. The renderer keeps a texture for as long as it is handed the same
picture, so a knob turned beside a Text uploads nothing. Typing into the box
bakes once per keystroke, which for a caption is well under a frame.

An atlas holds 64 lines of 64 letters, and a compile warning says so when there
are more. There are no glyphs beyond ASCII and no proportional spacing. Another
font is a sheet and one line in the list.

A stroke is one pixel wide, so the distance inside one is a ridge half a pixel
high. A read between texels a quarter-pixel apart rounds the top of that ridge
off. The edge, which is what a Fill draws, is exact.
