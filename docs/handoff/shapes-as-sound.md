# Shapes as sound: a Path module for oscilloscope music

Written on 2026-10-06, on `ccr-d0783b4d-xbzavr` at `4e97b0b`, the commit that added the Beam (ADR-0181). It is on TODO.md, parked; take it off there, and delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked by the user before any code

## What is wanted

The other direction from the Beam: read a drawing, a 3D model or a picture, and play it as stereo sound that draws that shape on a Beam, as Jerobeam Fenderson and Hansi Raber's OsciStudio does from Blender.

## The math

1. **Geometry to strokes:** a model's edges, an SVG's curves, or outlines traced from a picture.
2. **3D to 2D:** rotate, then divide by depth for perspective. Cheap enough to do every sample.
3. **Strokes to one path:** chain them nearest neighbor first, so the jumps between them are short. The Beam already draws a jump faint.
4. **Resample at constant speed,** by arc length, so every part of the shape is equally bright.
5. **Play it as a waveform:** one trip round the path is one cycle, x left and y right, and the trip rate is the pitch.

Steps 1, 3 and 4 are done once, as the file loads. Steps 2 and 5 are the patch.

## The shape in Flyback

- **A Path module**, with an extra that loads the file as the Image module's does, into three tables: x, y and z. The speakers' program already reads tables; a Sample plays one.
- It reads them with a phase accumulated at `freq` (ADR-0030), so a Note, MIDI or a Sequencer plays the shape as a melody.
- Outputs `x`, `y`, `z`. Rotation, perspective, wobble and morphing are ordinary modules downstream, so an LFO spins a model as it plays.

## Inputs, in order of cost

| Input | How |
|---|---|
| SVG | Its paths are paths: parse M, L, H, V, C, Q, Z. |
| OBJ | Edges from faces, deduplicated; projected in the patch. Hidden lines show, wireframe style. |
| PNG | Threshold, trace contours (marching squares), simplify, chain. Line art reads; a photo becomes a mess of contours, as in every such tool. |

## Limits to say up front

- Detail costs brightness: a longer path at the same pitch spreads the same beam thinner.
- No blanking without a third channel; jumps between strokes stay faintly visible, as on a real scope.
- A loaded file is outside input: the parsers cap sizes and never throw (`docs/agents/rules/security.md`).

## Sequence

1. Path module with SVG, an ADR, a scenario, and a `flyback-cli render` still on a Beam to check it.
2. OBJ, with a teaching preset spinning a model.
3. PNG contour tracing.
