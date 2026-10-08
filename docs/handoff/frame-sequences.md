# Frame sequences: a Path that plays an animation

Written on 2026-10-08, against `main` at `daa972f0` with the Drawings plugin on top. It is on TODO.md; take it off there, and delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open

## What is wanted

In oscilloscope music the animation is the music: the picture moving is the sound changing. A Path plays one drawing, and the modules after it (Rotate 3D, Scale 3D, a Mix between two Paths) animate it by moving or blending it. What is missing is the way OsciStudio works from Blender: a drawing per frame, played in order, so a figure can walk, a logo can draw itself in, or a model can deform.

## The shape in Flyback

- **A Path names a numbered sequence**: `walk_###.obj` or `logo_###.svg`, the `#`s standing for the frame number with its zeros, read as every file that matches, in order. A plain name stays one drawing, so nothing about today's Path changes.
- **A `frame` socket**, added at the end of the Path's inputs (socket order never changes; `docs/agents/rules/saved-data.md`). Its value picks the frame, wrapping past the last. `t * 24` plays 24 frames a second, and a sequencer or a Tempo plays it on the beat.
- **Frames change at the end of a trip round the drawing**, never mid-stroke, so a frame change never tears the shape across the screen. The table reads which frame to play from `frame` as it was at the start of the cycle, held in a cell per Path.
- **Each frame is laid out as today**: fitted, chained nearest first, spaced evenly. A sequence is fitted as a whole, one box and one center for every frame, so a figure that walks across the screen moves rather than being re-centered each frame.
- **The tables**: one table per axis holding every frame end to end, each frame its own point count padded to the longest, read at `(frame + phase) / frames`. Then the GPU (ADR-0167) and every backend read it with no new op.

## What to check first

- **Size.** 4,096 points × 3 axes × 4 bytes is 48 KB a frame; 1,000 frames is 48 MB. Cap the frame count, perhaps at 2,000, and the total points, and refuse past either by name, as the readers already do.
- **Library.** `ISampleLibrary.FindFile` takes one path. A sequence needs the host to list the matching files beside the patch, and a bundle to carry all of them; a `FindFiles(pattern)` default member, or the plugin asking for each name in turn until one is missing.
- **Holding the frame per cycle** needs the Path to keep a cell: how a plugin takes one is in plugin-guide.md §6. A Path drawn on the screen rather than heard has no cycle, and switching there on `frame` directly is probably right.
- **Blender export.** OsciStudio's own exporter writes per-frame OBJs; check a real export's naming and that one OBJ per frame is what comes out.

## Sequence

1. Sequence naming and loading, with `frame` switching immediately; a teaching preset of a few hand-made frames.
2. Switching only at a cycle's end.
3. A crossfade between neighboring frames as `frame` passes between them (a fraction of the way between two frames is a Mix of the two tables), so slow animations morph rather than step.
