# ADR-0077: The picture is heard only through a Scan

**Status:** Accepted · 2026-09-16 · *user-directed* · implemented in
`Graph/NodeCatalog.cs`, `Graph/NodeCatalog.Output.cs`,
`Graph/NodeCatalog.Sources.cs`, `Render/AudioRenderer.cs`,
`Render/MovieRenderer.cs`, `Language/Binder.cs` and `Language/PatchPrinter.cs` ·
supersedes [0043](0043-a-scan-is-a-probe-read-backwards.md)'s ruling that the
Output's `scan` knob stays · amends
[0037](0037-one-output-block-that-every-patch-has.md), whose Output loses `scan`
and `scan rate`

## Context

[0043](0043-a-scan-is-a-probe-read-backwards.md) gave the picture a module to be
heard through and kept the Output's `scan` knob beside it, calling the two
different instruments: a raster across the whole render, and a loop through one
branch. The raster is worth keeping as a sound. The question is whether a knob is
the thing that has to make it.

As a knob it cost more than it gave:

- **It was a switch drawn as a slider.** `scan` ran 0 to 1 and meant *on* from
  0.5, so most of its travel did nothing. `scan rate` did nothing at all unless
  `scan` was on.
- **It was silent in most patches, and nothing said so.** It moved where the
  whole audio program was evaluated. A patch whose `left` depended only on
  time sounded the same at every position, and a patch that wanted the sweep had
  to hear it everywhere, with no way to mix one scanned branch with an
  oscillator.
- **It was read in two places, two ways.** `AudioScan.For` looked the knobs up
  by position for offline renders, and `AudioEngine.ScanFor` looked them up by
  name for the live one. That is the silent kind of drift
  `AudioScan.For`'s own remarks warned against. The result was also threaded
  through every `AudioRenderer.Render` call in the engine, the shell, the CLI
  and the assistant.
- **No shipped preset used it**, in the engine or in any plugin.

Everything the knob did, a Scan could already do, except for one thing. At
`radius` 0 a Scan's loop is a single point at (`x`, `y`). Those are ordinary
sockets evaluated on the outer domain, so driving them from `t` gives any path at
all, a raster included. The one thing missing was **how far `x` reaches**. The
knob multiplied its sweep by the frame's aspect. A patch had no way to read that
number, even though the program already had an op for it: `LoadAspect`, which
the Probe charts use to find the edges.

## Decision

**The Output is `color`, `left`, `right` and `gain`.** `scan` and `scan rate` are
gone, and `SinkKind.Speakers` resolves the three sockets that are left.

**The speakers are always at the origin.** `AudioRenderer.Render` evaluates every
sample at (0, 0), and `AudioScan` is gone. Where the ear looks is decided
inside the program, per branch, by a Scan.

**Coordinates has a fifth output, `aspect`.** It is `LoadAspect`, the same
everywhere on the frame. It is appended after `angle`, so every existing wire
out of Coordinates keeps its meaning. The text language gains `aspect` as a bare
word alongside `x`, `y`, `radius` and `angle`, so it names the shared
Coordinates like they do, and the printer writes that wire back as the word.

**The aspect belongs to whoever renders, as a property of the renderer.**
`AudioRenderer.Aspect` defaults to 1 and is set once at construction:

| Renderer | Aspect |
|---|---|
| Movie export | the frame being written (`MovieSettings`) |
| CLI | the shape given by `--size`, including for a `.wav` render |
| Shell's sound export | `ExportSize`, 16:9 |
| Live engine | 16:9, the sound export's shape, so a patch sounds the same played as written |
| Assistant's listen | the frame limits it draws at |

**The raster is a patch, not a mode on the Scan.** The circle stays the
load-bearing choice [0043](0043-a-scan-is-a-probe-read-backwards.md) made it,
and for the same reason: the X-Y display needs the point of the path at a pixel's
bearing, and only a circle answers that in closed form. What used to be the knob
is:

```
let across = (fract(t * 60) * 2 - 1) * aspect
let down   = 1 - fract(t * 0.5) * 2

scan(field, radius: 0, x: across, y: down) |> out.left
```

`ScanTests` pins that this is heard sample for sample as the field read at
exactly those positions, which is what the knob's host-side loop did.

## Consequences

**A file saved with `scan` on no longer sounds as it did, and nothing says so.**
Its two extra knob values are ignored, since every reader indexes by the
definition. A wire into either socket is neither drawn nor compiled. The sound
is the picture at the origin, which for most such patches is a constant, and
the DC blocker then removes it. This is the trade
[0037](0037-one-output-block-that-every-patch-has.md) already made for its own
sockets. No rewrite rule maps the knob onto a Scan, because nothing shipped is
affected and a rewrite would have to split a render-wide sweep across every
branch reaching `left` and `right`.

**Every Coordinates emits one more op, whether or not `aspect` is read.** This is
the same no-pruning trade 0043 accepted for a Scan's display. Every preset
snapshot that reads a position, through a wire or a socket normalled to
Coordinates, gained one `uAspect` line and had its registers renumbered past it.
Nothing else in them changed.

**`aspect` is a reserved word in the language.** A bare `aspect` always means
Coordinates, so a `let aspect = …` can be written but never read back by that
name. The printer does not use it as a name.

**A patch reading `aspect` hears the renderer's frame, not the window's.** The
live engine plays 16:9 whatever shape the preview is. A movie export hears the
frame it writes, which is the preview's resolution. Before this, the shell's
movie export passed `ExportSize`'s 16:9 while writing at the preview's size.
Reading the frame from `MovieSettings` makes the sound and the picture of one
file agree.

**Hearing the picture now always takes a module.** That is more wiring than
turning a knob, and it is also the only form that can be mixed, modulated, and
seen on the canvas.
