# Flyback

A patchable video synthesiser for .NET 10. Images are generated the way an
analogue synth generates sound: oscillators, maths and feedback, evaluated per
pixel. Nothing is drawn — every frame is a function of `(x, y, t)`.

```
Coordinates ──▶ Sine ──┐
                        ├──▶ Add ──▶ Remap ──▶ HSV ──▶ Output
Coordinates ──▶ Sine ──┘
```

## Running it

```bash
dotnet run --project src/Flyback.App -c Release
```

## How it works

A patch is a graph, but the graph is never walked while rendering. It compiles
to a flat, straight-line program over `float` registers:

```
Patch (nodes + wires)
  └─▶ PatchCompiler      walks back from Output, topologically
       └─▶ Op[]          e.g. r7 = Mul(r4, r6)
            └─▶ SynthRenderer   evaluated once per pixel, rows in parallel
```

This matters for three reasons: the inner loop has no virtual dispatch and no
allocation; anything the Output node can't reach is never compiled, so unused
modules cost nothing; and because each op is one line of arithmetic, the same
op list is what a future GLSL backend would emit rather than interpret.

Roughly 2 ms per 960×540 frame on a desktop CPU, so the preview has plenty of
headroom at 60 Hz.

### Coordinates

`y` runs −1 (bottom) to 1 (top). `x` is the same scale widened by the aspect
ratio, so a circle stays a circle. `t` is seconds since the patch started.

### Feedback

The renderer keeps the previous frame in linear float RGB, and the `Feedback`
module samples it. Route it back towards Output through `Rotate` or `Scale` and
you get the camera-pointed-at-its-own-monitor tunnel — the "Feedback tunnel"
preset does exactly that. Values are clamped to 0..1 each frame, which is what
stops the loop running away.

## Sound

The same modules drive the speakers. Add an **Audio Output** module, patch
something into `left`, and press **Audio on**.

Nothing about the catalogue changes: audio is the same machine with only `t`
varying and a scalar coming out instead of a colour. Compilation is rooted at a
sink, so one patch produces one program per sink — and each pays only for the
modules it actually reaches. A noise field feeding the screen costs the speakers
nothing.

| | |
|---|---|
| Pitch | patch **Frequency** into an oscillator's `freq` — it is a knob in hertz rather than the single digits the visual modules use |
| Stereo | leave `right` unpatched and it carries `left`, the way a normalled jack does |
| `scan` | at 0 the patch is driven by Time; at 1 it sweeps the image and you hear the picture, at `scan rate` sweeps per second |
| Export | **Render audio…** writes 10 seconds to a WAV |

The **Drone** preset is the demonstration: one slow oscillator sets both the hue
of the image and the tremolo on the tone, so the two sinks are visibly and
audibly the same signal.

Audio runs at 48 kHz, 4× oversampled and filtered before decimation, which keeps
the naive `Saw` and `Square` from folding harmonics back down as buzzing. It
reduces aliasing rather than removing it. While sound plays it is the master
clock and the picture follows, since audio cannot be stretched to catch up.

Sound output is Windows-only for now (WASAPI); everything else, including WAV
export, is portable.

## Using the editor

| | |
|---|---|
| Add a module | click it in the left palette |
| Patch | drag from one socket to another |
| Unplug | drag a connected input away |
| Pan / zoom | drag the background or right-drag / mouse wheel |
| Frame the patch | `F` |
| Delete a module | `Delete` |
| Set exact values | select a module, use the inspector on the right |

Any input with nothing plugged into it uses the value shown on the node, so most
patches need no constant modules at all.

## Layout

| Project | |
|---|---|
| `src/Flyback.Core` | graph model, compiler, renderer, PNG writer — no UI dependency |
| `src/Flyback.App` | Avalonia editor and live preview, built in C# without XAML |

Why it is built this way is recorded in [docs/adr](docs/adr) — 21 decision
records covering the compiler, the renderer, the shell and the boundaries
between them.

## Tests

```bash
dotnet test Flyback.slnx
```

| Project | |
|---|---|
| `tests/Flyback.Core.Specs` | Gherkin scenarios (Reqnroll) for the behaviour the ADRs specify — dead code, cycles, port typing, input defaults, feedback |
| `tests/Flyback.Core.Tests` | snapshot tests of the rendered presets, plus property and fuzz tests over the compiler and interpreter |

Each `.feature` file names the ADR it pins, so the records and the specs stay
honest about each other.

Snapshots compare decoded pixels rather than file bytes, because PNG compression
and `MathF` results can both shift without any behaviour changing. When output
legitimately changes, inspect the `.received.png` next to its `.verified.png`
baseline and rename it to approve.

The fuzzer generates random well-formed patches and pushes them through compile
and render. It is the only test that reaches all 52 modules, and it is what
guards the gap ADR-0008 describes: nothing links a module's declared ports to
what its emit function actually indexes.

## Adding a module

Everything about a module lives in one entry in `NodeCatalog`: its sockets and
the ops it lowers to. Nothing else in the pipeline needs to change — it shows up
in the palette and compiles on its own.

```csharp
new NodeDef(
    "pattern.rings", "Rings", "Pattern",
    [Num("x"), Num("y"), Num("freq", 4f, 0f, 32f), Num("offset")],
    [Num("out")],
    (em, i) =>
    {
        var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
        return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
    },
    "Concentric sine rings.")
```

Ports typed `Any` pass through whatever arrives, which is how one `Multiply`
works on both a scalar and a colour.

## Files

Patches save as JSON (`.fbk`). `Save frame…` renders the current moment at
1920×1080 and writes a PNG.
