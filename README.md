<img src="docs/logo.svg" width="88" align="right" alt="">

# Flyback

A patchable video synthesiser for .NET 10. Images are generated the way an
analogue synth generates sound: oscillators, maths and feedback, evaluated per
pixel. Nothing is drawn — every frame is a function of `(x, y, t)`.

The mark is a sawtooth: the ramp that sweeps a CRT line and the fast retrace
back to start the next one — the flyback the program is named after, and one of
its own oscillators besides.

```
Coordinates ──▶ Sine ──┐
                        ├──▶ Add ──▶ Remap ──▶ HSV ──▶ Output
Coordinates ──▶ Sine ──┘
```

## Running it

```bash
dotnet run --project src/Flyback.App -c Release
```

## Publishing

```bash
dotnet publish src/Flyback.App -c Release -r win-x64 --self-contained -o artifacts/win-x64
```

`win-x64`, `win-arm64`, `osx-arm64`, `osx-x64` and `linux-x64` all work, from any
of them — the engine and the shell are portable, and the parts that are not are
plugins. Each build gets the plugins for the platform it is *for*, not the one it
was made on: a macOS publish contains CoreAudio and no NAudio, a Linux one ALSA
and neither, a Windows one WASAPI and nothing else.

Anything cross-published from Windows onto a Unix arrives without an executable
bit, because the filesystem writing it has no concept of one — `chmod +x Flyback`
once, at the other end.

macOS runs a bundle rather than a folder, so publishing for an `osx-*` identifier
also lays out `Flyback.app` beside the publish output:

```bash
dotnet publish src/Flyback.App -c Release -r osx-arm64 --self-contained -o artifacts/osx-arm64/publish
# → artifacts/osx-arm64/Flyback.app
```

A bundle cross-published from Windows needs one more thing besides that mode: the
signature macOS on Apple silicon demands before it will run anything at all.
Once, on the Mac:

```bash
chmod +x Flyback.app/Contents/MacOS/Flyback && codesign --force --deep --sign - Flyback.app
```

Publishing on macOS itself sets the mode, and leaves only the signature.

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

Rendering a frame is the expensive thing here, not making sound. On a 12-core
desktop a 960×540 frame of the Plasma preset costs about 23 ms of wall time and
127 ms of CPU across the rows, so the preview settles below 60 Hz on anything
elaborate — while a second of audio for the same patch costs around 6% of one
core.

That asymmetry is why the renderer leaves one core alone and why the preview
rests between frames in proportion to what the last one cost. Sound has a
deadline and a picture does not: a dropped frame is invisible, and a late audio
buffer is a click.

### Coordinates

`y` runs −1 (bottom) to 1 (top). `x` is the same scale widened by the aspect
ratio, so a circle stays a circle. `t` is seconds since the patch started.

### Feedback

The renderer keeps the previous frame in linear float RGB, and the `Feedback`
module samples it. Route it back towards Output through `Rotate` or `Scale` and
you get the camera-pointed-at-its-own-monitor tunnel — the "Feedback tunnel"
preset does exactly that. Values are clamped to 0..1 each frame, which is what
stops the loop running away.

### Nebula

The preset worth reading the patch of. Coordinates are turned, folded into eight
wedges, bent by a noise field, taken as travelling rings, and laid over a
dimmed, slowly rotating copy of the previous frame.

The order of the fold and the bend is the whole trick, and it is not the obvious
one. Warping *before* folding looks lovely and is not a kaleidoscope at all: the
displacement varies per pixel, so the fold has nothing symmetric left to work
with and the eight-fold structure disappears. Folding first and warping inside
the wedge — by a field read from the folded plane, so it repeats with it — keeps
the symmetry and bends it at the same time.

At 79 ops it is the most expensive patch here, which is the other reason to keep
it: it is what the renderer looks like under load.

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
code in it at all. WASAPI covers Windows, CoreAudio covers macOS and ALSA covers
Linux — routed by PipeWire or PulseAudio where either is running — and each ships
only in the build for its own platform. Where none of them can open a device the
**Audio on** button is disabled and says why; everything else, including WAV
export, works unchanged.

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
public sealed class JackPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("jack", "JACK output", "Low-latency Linux sound via JACK.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new JackOutput());
}

public sealed class JackOutput : IAudioOutput
{
    public string Id => "jack";
    public string Name => "JACK";
    public int Priority => 150;                         // the shipped ALSA backend is 100, and loses to this
    public bool IsSupported => OperatingSystem.IsLinux() && JackIsRunning();

    public IAudioDevice Create(AudioFormat format) => new JackDevice(format);
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
<PluginProject Include="..\Flyback.Plugins.Alsa\Flyback.Plugins.Alsa.csproj" FolderName="Alsa" Platform="linux" />
```

`Platform` is optional and names the one platform the plugin is for, matching the
leading part of a runtime identifier. A plugin that declares one is left out of
output built for anywhere else; a plugin without one goes everywhere, which is
what a module plugin wants.

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

### Delay and reverb

[`src/Flyback.Plugins.Space`](src/Flyback.Plugins.Space) adds **Delay** and
**Reverb**, and the **Echo chamber** preset puts a plucked tone through both.
They are the only modules in the synth that remember anything.

| | |
|---|---|
| Delay | `time` in seconds, up to two, and sweepable — the line interpolates, so it glides rather than steps. `feedback` is how much comes round again |
| Reverb | four feedback combs into two allpasses. `size` stretches every delay together, `decay` sets how long the tail lasts |

Everything else here is a pure function of `(x, y, t)`, which is what lets the
video renderer run rows in parallel. These two are not, so **they only work on
the audio path**: the video program is given no delay state, a Delay becomes a
wire, and a Reverb dims by one minus its feedback. That asymmetry is the price of
having them at all, and [ADR-0027](docs/adr/0027-delay-lines-give-the-audio-path-a-memory.md)
sets out why it cannot be avoided. For a picture with a past, use `Feedback`.

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
| `src/Flyback.Plugins.Wasapi` | Windows sound output, via NAudio |
| `src/Flyback.Plugins.CoreAudio` | macOS sound output, straight to the default output audio unit |
| `src/Flyback.Plugins.Alsa` | Linux sound output, through libasound's default device |
| `src/Flyback.Plugins.Supersaw` | the Supersaw oscillator, as a module plugin |
| `src/Flyback.Plugins.Space` | delay and reverb — the only modules with a memory |

Why it is built this way is recorded in [docs/adr](docs/adr) — 29 decision
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
| `tests/Flyback.Plugins.Tests` | loads the real shipped plugins off disk — discovery, which backend wins on this machine, and that a type crossing the boundary keeps one identity |

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
