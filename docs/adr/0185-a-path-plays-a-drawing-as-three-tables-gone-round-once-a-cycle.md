# ADR-0185: A Path plays a drawing as three tables gone round once a cycle

**Status:** Accepted · 2026-10-08 · *user-directed* · the other direction from
[0181](0181-a-beam-draws-two-taps-against-each-other-on-phosphor.md)'s Beam; names a
file as [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)'s Sample
does and reads it as [0179](0179-a-midi-file-plays-as-tables-read-by-the-clock.md)'s
MIDI File does

## Context

A Beam draws whatever the speakers play, but making the sound that draws a given
shape meant building it from oscillators by hand. Oscilloscope music tools
(OsciStudio, from Blender) start from the drawing instead: a model's edges, an SVG's
curves or a picture's outlines, chained into one path and played as stereo, x left
and y right.

## Decision

**A Path is an oscillator whose wave is a drawing.** It names an `.svg`, an `.obj` or
a `.png` and goes round it once a cycle at `freq`, the phase accumulated as a Sine's
is (0030), so a Note or a sequencer plays the drawing as a melody. Its outputs are
`x`, `y` and `z`, times `amp`. Turning, perspective, wobble and morphing are ordinary
modules after it, so the patch is where a model spins.

**The file becomes one closed path as it loads.** `ShapeReader` turns each format
into strokes, and `ShapeLayout` fits them to -1 to 1 (a model to the unit sphere, so
it stays in bounds as it turns), chains them nearest first, turning a stroke round
when its end is nearer, and spaces the result evenly by arc length into about 4,096
points. Even spacing is what makes every stroke equally bright, since a beam lays
down the same energy each moment. A jump between strokes is a single step, the
fastest a table moves, so a Beam all but hides it.

**A drawing is three tables.** `LoadedShape` holds x, y and z as `LoadedSample`s
whose rate is their point count, the first point written again at the end, so a
phase of 0 to 1 read as seconds goes once round and wraps without a seam. Both
programs read them (0167).

**The formats, and what each is read as:**

- SVG: every path, line, polyline, polygon, rectangle, circle and ellipse, curves and
  arcs flattened, transforms applied, drawn round its outline. Nothing under `defs`,
  a clip, a mask, a marker, a pattern or a symbol, nor anything `display="none"`.
- OBJ: the edges of every face and line, each drawn once, walked into as few trails as
  the mesh allows. A wireframe: hidden edges show.
- PNG: where the brightness crosses a half, traced by marching squares, simplified,
  specks dropped and the longest 10,000 outlines kept. Line art reads, each line
  round both its edges; a photograph is a mess of contours.

**The library is the sound library.** `SampleLibrary` and `BundleFiles` answer
`IShapeLibrary` beside `ISampleLibrary`, and a compile asks the library it was handed
whether it is one. Nothing on the plugin contract moves, since a plugin has no use
for a drawing it cannot emit.

**A file is hostile until read.** 16 MB, a million points and 10,000 strokes are the
caps, each refused by name; no DTD is read and no entity fetched; a malformed SVG
gives what came before the fault, and no reader throws.

## Consequences

- Detail costs brightness: a longer path at the same pitch spreads the same beam
  thinner, and a busier drawing wants a lower note.
- There is no blanking without a third channel, so a jump stays faintly visible, as
  on a real scope.
- What the speakers play passes their DC blocker (about 5 Hz) and the step-down
  filter, so on a real oscilloscope a slow drawing sags and a sharp corner rings. A
  Beam taps the Path before either, so it draws the drawing.
- A shipped preset carries its model in its assembly (0151), as Wireframe carries a
  cube.
