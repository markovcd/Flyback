# ADR-0213: A Raymarch is its scene written out once a step

**Status:** Accepted · 2026-10-25 · *user-directed* · unrolls as
[0005](0005-compile-to-a-flat-register-machine.md)'s machine requires, as the
Fractals' escape count does

## Context

Asked for the module that tests Flyback's limits hardest, the picture's limit was
the one nothing came near: a program has no loops, so a shader is as long as its
op list, and the longest, Mycelium's, is 63,000 characters and takes seconds to
link. A 3D scene drawn by ray marching is a loop of up to hundreds of steps, each
step the whole scene's distance function.

## Decision

**Raymarch is a module of the Picture plugin, written out a step at a time.** The
step count is a node setting of 16 to 256, because it decides which ops are built,
as an iteration count does on a Mandelbrot. A ray that has landed is frozen rather
than stopped, so every pixel runs every step and every value stays finite with no
branch. Each step costs about fifty ops: 64 steps is about 3,500 ops and a
120,000-character shader, 256 about 13,000 ops and 440,000 characters.

**The scene is a setting, its shape is knobs.** Three scenes, a lattice, a tunnel
and a gyroid, each a distance function of a few dozen ops; 'pulse', 'morph',
'twist' and 'spacing' reshape whichever is chosen. A scene is not wired in from
other modules: a distance function built from modules would be lowered once per
step, and the patch has no way to say "this, again, at the next point".

**The camera never collides.** The scene is built in the camera's frame and the
camera's axis runs down a path each scene leaves open, so 'travel' on a clock is a
flight that goes on for as long as the patch plays.

## Consequences

- The largest step counts are a stress test, not a default: 64 is, and the
  Flythrough preset plays at it.
- The processor draws a 720p frame at 64 steps in about half a second on four
  cores; a Raymarch is for the GPU.
- A loop in the op list would make a step count cost nothing to compile. That is a
  change to the machine every backend shares, and is not made here.
