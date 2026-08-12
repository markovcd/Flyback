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

The device itself comes from a plugin, so the application has no platform-specific
code in it at all. WASAPI ships in the box and covers Windows; elsewhere the
**Audio on** button is disabled until a backend is installed, and everything
else — including WAV export — works unchanged.

## Plugins

Anything that differs per machine is loaded from disk at startup rather than
referenced at build time. One folder per plugin, beside the executable:

```
Flyback.exe
plugins/
  Wasapi/
    Flyback.Plugins.Wasapi.dll
    Flyback.Plugins.Wasapi.deps.json
    NAudio.dll …
  Supersaw/
    Flyback.Plugins.Supersaw.dll
    Flyback.Plugins.Supersaw.deps.json
```

The status bar says which backend is playing; hover it for what loaded, what did
not and why, and where the folder is.

A sound backend is one class that offers itself and one that opens the device.
Being asked whether you are supported must not open anything, so a plugin for
another operating system stays loadable everywhere:

```csharp
public sealed class AlsaPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("alsa", "ALSA output", "Linux sound via libasound.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new AlsaOutput());
}

public sealed class AlsaOutput : IAudioOutput
{
    public string Id => "alsa";
    public string Name => "ALSA";
    public int Priority => 50;                          // WASAPI is 100, and wins where both run
    public bool IsSupported => OperatingSystem.IsLinux();

    public IAudioDevice Create(AudioFormat format) => new AlsaDevice(format);
}
```

`IAudioDevice.Start` is handed a callback that fills an interleaved buffer on the
audio thread; it must not block, allocate or throw. That is the entire contract —
`Flyback.Plugins` has no dependencies, and neither need you.

Reference `Flyback.Plugins` **and** `Flyback.Core` with `Private="false"
ExcludeAssets="runtime"` — both are host-owned, and naming only the first leaves
the second to be copied in as a transitive reference. Set `EnableDynamicLoading`
so the `.deps.json` the loader reads is produced, and drop the build output into
a folder under `plugins/`. To ship one in the box instead, add a line to
`Flyback.App.csproj`:

```xml
<PluginProject Include="..\Flyback.Plugins.Alsa\Flyback.Plugins.Alsa.csproj" FolderName="Alsa" />
```

Each plugin loads into its own `AssemblyLoadContext`, so two of them may depend
on different versions of the same package. A broken, missing or hostile plugin is
a note in the status bar, never a program that fails to start.

### Modules from a plugin

A plugin can also add modules. They are the same data as the built-in ones — the
`NodeDef` from *Adding a module* below, written in another assembly:

```csharp
public sealed class SupersawPlugin : IFlybackPlugin
{
    private static readonly ModuleProvider Provider = new("flyback.supersaw", "Supersaw");

    public PluginInfo Info { get; } = new("flyback.supersaw", "Supersaw");

    public void Register(IPluginRegistry registry) => registry.AddModules(Provider,
    [
        new NodeDef("flyback.supersaw.osc", "Supersaw", "Oscillator", …),
    ]);
}
```

Every type id must start with the provider's id and a dot. That rule is what
makes shadowing a built-in impossible, and what lets a saved patch name the
plugin a module came from without having the plugin to ask.

A plugin can offer **presets** too, which is how it ships a patch showing its
modules wired properly. They appear in the Patch dropdown after the engine's own
— the app always opens on `Plasma`, so what you see at startup never depends on
what is installed:

```csharp
registry.AddPresets([new PatchPreset("Supersaw", SupersawPreset.Build)]);
```

A preset is handed the catalogue when it is picked rather than when it is
registered, so it can freely use the modules the same plugin just added.

Because a patch is keyed by type id, one that uses a plugin's modules writes
down what it needs:

```json
{
  "Requires": [ { "Id": "flyback.supersaw", "Name": "Supersaw" } ],
  "Nodes": [ … ]
}
```

Open it somewhere that plugin is not installed and it does not open — you get
*"This patch needs Supersaw (flyback.supersaw)"*, and whatever you already
had stays where it was. A patch that uses only the modules in the engine records
nothing and is written exactly as it always was.

Plugins are read once at startup, so installing one means restarting.

### Supersaw

[`src/Flyback.Plugins.Supersaw`](src/Flyback.Plugins.Supersaw) ships in the box
and is the worked example — seven detuned saws in one module, under a hundred
lines.

| | |
|---|---|
| `detune` | spreads the voices apart, up to about ±1.4 semitones |
| `mix` | fades the six outer voices in against the centre — at 0 it is *exactly* a plain Saw |
| `out` / `wide` | the same voices at complementary weights; patch both for stereo, or use `out` alone |

Pick **Supersaw** from the Patch dropdown for it wired up: 110 Hz from a
Frequency module, both outputs to their own channel, and one slow sweep driving
the detune of the sound and of the picture at once, so the bands on screen beat
in step with what you hear. `freq` counts cycles per unit of `in` like every
other oscillator here — reach for its knob instead of a Frequency module and you
get a one-hertz saw, which is a click rather than a note.

Seven voices, not a knob's worth: an emit function runs once at compile time and
writes straight-line ops, so it never sees a knob value and a variable voice
count would mean a variable number of instructions. Seven is what the sound is.
The whole thing unrolls to about seventy ops, no branches and no state, and it
is peak-normalised as it mixes — turning `mix` up makes it wider, never louder.

Point it at the screen instead of the speakers and the detuning reads as a slow
moiré, which is the same beating seen rather than heard.

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
| `src/Flyback.Plugins` | the plugin contract and the loader — no dependencies either |
| `src/Flyback.Plugins.Wasapi` | Windows sound output; the only project that knows what an operating system is |
| `src/Flyback.Plugins.Supersaw` | the Supersaw oscillator, as a module plugin |

Why it is built this way is recorded in [docs/adr](docs/adr) — 26 decision
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
| `tests/Flyback.Plugins.Tests` | loads the real shipped plugin off disk — discovery, backend selection, and that a type crossing the boundary keeps one identity |

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
in the palette and compiles on its own. The same entry works from a plugin; see
*Modules from a plugin* above.

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
