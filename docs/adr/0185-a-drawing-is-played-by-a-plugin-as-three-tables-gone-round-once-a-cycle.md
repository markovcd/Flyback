# ADR-0185: A drawing is played by a plugin as three tables gone round once a cycle

**Status:** Accepted · 2026-10-08 · *user-directed* · the other direction from
[0181](0181-a-beam-draws-two-taps-against-each-other-on-phosphor.md)'s Beam; names a
file as [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)'s Sample
does and reads it as [0179](0179-a-midi-file-plays-as-tables-read-by-the-clock.md)'s
MIDI File does; ships as [0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)'s
site plugins do

## Context

A Beam draws whatever the speakers play, but making the sound that draws a given
shape meant building it from oscillators by hand. Oscilloscope music tools
(OsciStudio, from Blender) start from the drawing instead: a model's edges, an SVG's
curves or a picture's outlines, chained into one path and played as stereo, x left
and y right, and turned in 3D on the way.

## Decision

**Drawings is a plugin the preset site starts with**, beside Figures, Fractals and
Easy: Path, Rotate 3D, Translate 3D, Scale 3D and Perspective, each with artwork of
its own, and a Wireframe preset carrying its cube. The engine keeps no drawing code.

**A Path is an oscillator whose wave is a drawing.** It names an `.svg`, an `.obj` or
a `.png` and goes round it once a cycle at `freq`, the phase accumulated as a Sine's
is (0030), so a Note or a sequencer plays the drawing as a melody. Its outputs are
`x`, `y` and `z`, times `amp`.

**The 3D modules are a point in, a point out.** Rotate 3D turns by yaw, pitch and roll
in that order, Translate 3D adds, Scale 3D multiplies as a whole and per axis, and
Perspective divides across and up by distance from a camera on the z axis, scaled so
depth nought keeps its size and held in front of the camera rather than dividing by
nought. Chained, they are a model's whole trip to the screen, and a clock or an
oscillator on any socket animates it.

**The file becomes one closed path as it is parsed.** Strokes are fitted to -1 to 1 (a
model to the unit sphere, so it stays in bounds as it turns), chained nearest first,
turned round when their end is nearer, and spaced evenly by arc length into about
4,096 points, so every stroke is equally bright. A jump between strokes is a single
step, the fastest a table moves, so a Beam all but hides it. The three tables' rate is
their point count, the first point written again at the end, so a phase of 0 to 1
goes once round without a seam.

**The formats:** an SVG's paths, lines, polylines, polygons, rectangles, circles and
ellipses, curves and arcs flattened and transforms applied, nothing under `defs` or
hidden; an OBJ's face and line edges, each once, walked into as few trails as the
mesh allows; a PNG's outlines where brightness crosses a half, by marching squares,
specks dropped and the longest 10,000 kept.

**A plugin reads a file through the library it is handed.** `ISampleLibrary` gains
`FindFile`, a file's bytes, and `FindPicture`, a PNG decoded on either program, with
`ExplainFile`; the host resolves the path, caps a file at 16 MB, reads it once and
answers with the same object until it is forgotten, so the plugin caches its parse
against that object. A plugin's file extra derives from `FileExtra` with a public
`FileKind`, and gets the panel row, the text language, bundling, rebasing and the
assistant's `set_sample` with it.

**A file is hostile until read.** A million points and 10,000 strokes are the caps,
each refused by name; no DTD is read and no entity fetched; a malformed SVG gives what
came before the fault, and no parser throws.

## Consequences

- Detail costs brightness: a longer path at the same pitch spreads the same beam
  thinner, and a busier drawing wants a lower note.
- There is no blanking without a third channel, so a jump stays faintly visible, as
  on a real scope.
- What the speakers play passes their DC blocker (about 5 Hz) and the step-down
  filter, so on a real oscilloscope a slow drawing sags and a sharp corner rings. A
  Beam taps the signal before either, so it draws the drawing.
- Nobody has Drawings until they install it from the site, as with Figures.
- The plugin contract grows by `FileKind`, `FileExtra.Kind` and the three library
  members; the contract version stays at 1.0.0 while no plugin from outside exists.
