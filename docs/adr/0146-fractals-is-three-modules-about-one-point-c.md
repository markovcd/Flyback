# ADR-0146: Fractals is three modules about one point c

**Status:** Accepted · 2026-09-24 · *user-directed* · follows
[0142](0142-figures-is-three-modules-that-are-each-a-picture-and-a-sound.md) and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)
for how a plugin reaches anybody

## Context

A Mandelbrot set was asked for, in the colors it is known by, and then a
sound made the same way and a third module in line with the two, as a plugin
of their own that only the preset site publishes.

The program has no loops and no branches. An escape-time fractal is a loop
that stops early.

## Decision

**Three modules that all name c by 're' and 'im'.** Mandelbrot maps every c,
with 're' and 'im' the c in the middle of the picture. Julia is the picture of
one c. Orbit is its sound: z → z² + c stepped at 'rate', the real part on the
left and the imaginary on the right, gliding from one point to the next. One
pair of knobs patched into all three is one point, found, seen and heard.

**The iterations are unrolled, and their count is carried on the node**, as a
Fractal's octaves are: 16 to 256, 64 by default, about twelve ops each. An
escaped z is frozen by a Mix on the step it left, not iterated on. That keeps
every value finite without a branch, and leaves the escaped z in hand for the
smooth count, which is what removes the bands. The bailout is |z|² = 256
rather than 4, so the smooth count has no seams.

**The classic gradient is built in**, as a `color` output. It is one cosine per
channel, navy through white and gold, cycled by 'shift', with the set black.
`escape`, 0 to 1, is there for a Palette of anybody's own.

**Orbit has a Mandelbrot mode and a Julia mode, as a setting on the node.**
The Mandelbrot orbit of c is the Julia orbit of nought, so the modes are one
iteration with two starts: nought, or the point on 'start re' and 'start im', a
pixel of the Julia set of c. The mode also picks the plane the path is drawn
on, the Mandelbrot's at rest or the Julia's, so the path lines up drawn over the
module of the same name. It is a setting rather than a socket because a plane
is not something to sweep.

**Orbit keeps its steps in cells.** Only the speakers have them. On the screen
the path is iterated afresh, 24 steps. An orbit that escapes goes back to its
start, so an escaping one is a tone too, one period for each step it took to
leave. Each side goes through a DC blocker at twenty hertz, because an orbit
rarely centers on nought.

**Each module's artwork is drawn by the module itself**, as PNGs darkened so
white labels read over them. The `.fbks` that drew each one is kept beside it.

**The preset site starts with it, like Figures**: it is a `PluginProject` in
`Flyback.Server.csproj`, the one list the site's image and `release.sh` pack
and sign from. No release carries it.

## Consequences

- The picture is float on the shader, so a zoom past about twelve halvings
  turns to blocks. A deep zoom would need two floats a number, and is not here.
- Mandelbrot at 64 iterations is about 830 ops a pixel, at 128 about 1,600.
  That is trivial on the graphics card and Cells' price on the processor.
- Orbit's picture program carries its cells and phase, reading nought, because
  an emit function cannot know its sink.
- Dive zooms the Mandelbrot into Seahorse Valley and back. Julia walk takes one
  c round |c| = 0.7885: its Julia set on the screen with a Julia-mode Orbit's
  path summed over it, and that orbit in the speakers.
