<img src="docs/logo.svg" width="88" align="right" alt="">

# Flyback

A patchable synthesiser for .NET 10 that makes a picture and a sound out of one
patch. Images are generated the way an analogue synth generates sound:
oscillators, maths and feedback, evaluated per pixel. Nothing is drawn — every
frame is a function of `(x, y, t)`, and every sample is that same function with
only `t` moving.

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
dotnet publish src/Flyback.App -c Release -r win-x64 -o artifacts/win-x64
```

What comes out is three things and nothing else:

```
Flyback.exe          the program, the runtime it needs, and every library either uses
Flyback.Core.dll     ┐ the plugin boundary
Flyback.Plugins.dll  ┘
plugins/             one folder per plugin
```

The executable is self-contained, so the machine it lands on needs no .NET
installed, and it is a single file: the assemblies are bundled into it, and so
are the native libraries Skia and HarfBuzz bring, which the program unpacks to a
temporary directory the first time a given build is run. Symbols are not
published — they are in the build output, where a debugger looks for them.

The two in the middle are deliberately left beside the executable rather than
bundled into it, because a copy inside a single file is a copy nothing can be
compiled against — and those two are what a plugin is compiled against. They are
the pair `PluginLoadContext` calls host-owned, under 300 KB together, and they
are still the host's own at run time: a plugin's load context refuses to resolve
a host-owned name itself, so the contract keeps one identity wherever it is
shipped from.

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
dotnet publish src/Flyback.App -c Release -r osx-arm64 -o artifacts/osx-arm64/publish
# → artifacts/osx-arm64/Flyback.app
```

A bundle cross-published from Windows needs one more thing besides that mode: the
signature macOS on Apple silicon demands before it will run anything at all.
Once, on the Mac:

```bash
chmod +x Flyback.app/Contents/MacOS/Flyback && codesign --force --deep --sign - Flyback.app
```

Publishing on macOS itself sets the mode, and leaves only the signature.

## Building with Docker

The whole release without a .NET SDK on the machine: restore, compile, every
test in the solution, and one self-contained publish per platform.

```bash
docker build --output artifacts .
```

That leaves `artifacts/win-x64` and `artifacts/linux-x64` — each the executable,
`Flyback.Plugins.dll` and `plugins/`, as above — and `artifacts/osx-arm64`,
holding `Flyback.app` and nothing else, since the bundle is a complete copy of
the payload it was laid out from. The tests are a step in the build rather than
something you run afterwards, so a red one fails the `docker build` and nothing
is written.

The other two identifiers are an argument rather than an edit, and each is a
whole copy of the runtime, so ask for what you need:

```bash
docker build --build-arg RIDS="win-x64 win-arm64 osx-arm64 osx-x64 linux-x64" --output artifacts .
```

Building on Linux is also how the executables get their mode — the thing a
cross-publish from Windows cannot do. `--output artifacts` onto an NTFS drive
loses it again, so to carry a Unix build to a Unix machine, take the tar:

```bash
docker build --output type=tar,dest=flyback.tar .
```

The macOS signature is still the one thing that has to happen on a Mac.

Leave `--output` off and the build is a pure check — everything compiles,
everything passes — which is what it is worth running in CI.

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

### Looking at a value

Patch an output into a **Probe** and select it, and the screen shows a chart of
that value instead of the picture — the middle column is now, the left is the
past and the right is the future, which a machine that is a pure function of `t`
can draw as readily as its own history. `window` is how much time the chart
covers and `scale` is the value at the top edge; one grid square is an eighth of
the first and a quarter of the second. The timebase is marked in decades, the way
a scope's is, because the same knob has to reach from one cycle of an audible
tone to half a minute of an LFO — it reads as the time it is, from `100 µs` to
`31.62 s`, and a signal patched into it sweeps the chart exponentially. Select anything else and the picture comes
back. The sound is never interrupted, because the speakers root at the Output
whatever the screen has been asked for.

Nothing is measured and nothing is read back: a chart of a signal is itself a
function of `(x, y, t)`, so the probe is an ordinary module compiling to ordinary
ops, and the GPU backend draws it without being told it exists
([ADR-0040](docs/adr/0040-a-probe-is-a-second-compile-root.md)). What makes it
work is that its input is read over a *different* domain from the pixel drawing
it — time swept across the picture, x and y pinned to the middle — so what you
get is the signal at the centre of the screen over a couple of seconds, not the
field it makes.

The one thing it cannot show is memory. Drawn rather than heard, an oscillator
does not accumulate its phase and a delay line passes straight through, so a
chart of either is what the screen makes of it rather than what the speakers do.
Its own `out` is the chart as a colour, so it can also be patched into the Output
and left on screen beside the thing it is watching.

## Sound

The same modules drive the speakers. Patch something into the **Output**'s
`left`, select that block, and press **Audio on** in its panel.

Nothing about the catalogue changes: audio is the same machine with only `t`
varying and a scalar coming out instead of a colour. Compilation is rooted at a
sink, so one patch produces one program per sink — and each pays only for the
modules it actually reaches. A noise field feeding the screen costs the speakers
nothing.

Screen and speakers are two halves of one block rather than two modules, and
every patch has exactly one of it: it is not in the palette, it cannot be
deleted, and everything about seeing or hearing the patch lives in its panel.
What each half costs is still separate — the picture's program walks back from
`colour` alone and the sound's from `left` and `right`, so neither pays for the
other.

| | |
|---|---|
| Pitch | patch **Frequency** into an oscillator's `freq` — it is a knob in hertz rather than the single digits the visual modules use |
| Notes | **Note** is the same thing in notes rather than hertz. Pick one on the knob — it reads as `A3`, not as 57 — or patch a signal in and it snaps to the nearest whole note, which is what turns a sweep into a run up the chromatic scale. `octave` transposes by twelve semitones a step, `cents` detunes past the snap, and `note` hands the snapped number on so a second **Note** can play an interval off it |
| Sequences | **Note Sequencer** is a list of notes, edited in the inspector — `out` to a **Note** for the pitch, `gate` into a multiply so a rest is heard as one, `index` to the screen. **Sequencer** is the same list as plain signals instead. `shape` sets how long the gate takes to open and close |
| Writing a tune | up to 32 notes, added and removed anywhere in the list and dragged into any order. Each carries a **length** in steps, so a note can be held twice as long as its neighbour, and a **volume** — a level rather than a switch, so it silences a note without losing it and doubles as its velocity |
| Mixing | **Mixer** sums four inputs, each through its own level, where a row of Multiplies into a chain of Adds used to be. Levels are sockets like everything else here, so a fader is something an oscillator can sweep as well as something a hand can set — and because its sockets are untyped, the same module mixes four pictures as readily as four tones. The **Four voices** preset is it wired up at both sinks at once |
| Stereo | leave `right` unpatched and it carries `left`, the way a normalled jack does |
| `scan` | at 0 the patch is driven by Time; at 1 it sweeps the image and you hear the picture, at `scan rate` sweeps per second |
| Export | **Export…** writes a WAV of the sound or an AVI of both — see *Files* |

The **Drone** preset is the demonstration: one slow oscillator sets both the hue
of the image and the tremolo on the tone, so the two sinks are visibly and
audibly the same signal.

**Chromatic** is the same idea through a **Note**. One ramp is snapped to whole
notes and sent to both sinks, so what the ear hears as a run of separate notes
the eye sees as flat rings of colour — and because the audio path pins `x` and
`y` to zero, the module the speakers hear one note from is showing the screen the
fourteen either side of it. Brightness is taken from the ramp before the snap, so
a smooth glow and the rings sitting in it are the before and after of the same
signal.

**Sequence** is a tune rather than a scale. Eight notes in a list go to the
speakers, and the sequencer's other two outputs go to the screen: `index` picks
the hue and the number of rings, so the picture reorganises itself on the beat,
and `gate` dims it exactly where it silences the note, so the rhythm is visible.
Nothing is duplicated between the two sinks — every difference between what the
ear gets and what the eye gets is a different output of the one module.

Its gate ramps rather than switches, which is not a nicety. ADR-0030 stopped a
stepped *pitch* clicking; a stepped *amplitude* is the same failure from the
same cause, and nothing was catching it — the first version of this preset tore
its waveform at nineteen times the wave's own travel, six times a second.
Oversampling band-limits a discontinuity rather than removing one, so the fix is
an envelope: four ops, no state, and the edges are a fraction of a step so they
scale with the tempo.

A sequencer needs no memory, which is why it is one of the few things here that
behaves identically at both sinks. Which step is playing is a function of where
its `in` has got to, not of what played before, so unlike a delay (ADR-0027) or
an accumulated phase (ADR-0030) there is nothing to carry and nothing a
recompile can disturb. `in` is a domain rather than a clock for the same reason
an oscillator's is: stop it and the sequence stops, run it backwards and it runs
backwards.

The notes themselves are a list on the module rather than knobs on it, which is
what lets a pattern be any length and a note any duration
([ADR-0038](docs/adr/0038-a-sequencers-notes-are-a-list-on-the-node.md)). That
list is the one thing here a module carries that is not a knob, and it costs the
one thing the old row of sockets could do: a step is no longer something you can
patch into. Where every note is the same length the module compiles to exactly
the ops it always did; only an uneven pattern pays for being uneven.

**Four voices** is the **Mixer** wired up, and it is laid out as four channel
strips because that is what it is. Each row is one voice: a note and a sine at it
for the ear, the same oscillator read across the radius instead of across the
clock for the eye, and one slow sine setting both of their levels. The faders are
the only thing the two sinks share — level three on the chord is level three on
the screen — so what fades up in the sound is visibly the thing that fades up in
the picture.

There are two Mixers in it and only one module: the chord sums four scalars, the
picture sums four colours, and both do it with the same four multiplies and three
adds, because the sockets are untyped like the maths modules'. What differs is
what each sink does with a sum that overflows. The Output's gain is a quarter,
which is exactly four voices at full and puts the worst case at full scale rather
than past it; the picture's Gain is far more generous, since light that runs over
clips to white and reads as brightness, while sound that runs over reads as a
fault.

### Why a stepped pitch does not click

It very much used to. An oscillator's phase was `in × freq`, which is the natural
shape for a machine that is otherwise a pure function of `(x, y, t)` — a picture
has no previous sample to carry a phase in (ADR-0005, ADR-0006). But phase is the
integral of frequency, not its product with time, and the two only agree while
`freq` holds still. Step `freq` at time `t` and the product jumps by `t` times the
change: ten seconds in, one semitone at A3 skips about 131 whole cycles and the
wave restarts wherever that lands. The tear is the click, and it grows with the
clock.

Smoothing the pitch cannot fix that, and trying is instructive. The pitch actually
produced is `freq + t × freq′`, so a ramp across each step trades an infinite
`freq′` for a finite one multiplied by `t` — a wobble of nearly 2 kHz on a 220 Hz
tone, ten seconds in. A *narrow* ramp is worse than no ramp: the same excursion,
held longer. Only a ramp wide enough to be a portamento sounds clean, at which
point the quantiser is not one.

So the oscillators accumulate instead (ADR-0030). Each keeps a running phase and
advances it by however far its `in` moved, in cycles of `freq` as it is *now*. A
frequency that jumps moves the phase by one ordinary step regardless, so the
wave's value carries straight across the change and only its slope differs — and
a slope has no click in it. Nothing about the pitch is smoothed. Measured on
Chromatic as the largest sample-to-sample jump against the median one, over a
second of raw program output:

| measured from | multiplied | accumulated |
|---|---|---|
| 0 s | 237× | 1.6× |
| 20 s | 538× | 1.8× |
| 60 s | 648× | 2.6× |

`in` stays the socket it was: the step is measured from the input rather than
counted off a clock, so a domain that stops stops the tone and one running at
twice the rate doubles the pitch, exactly as the multiply did. Drawn rather than
heard, the accumulator *is* the multiply — one evaluation per pixel has nothing
to accumulate — so an oscillator's picture is unchanged to the byte.

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
Besides an oscillator's running phase, they are the only things in the synth that
remember anything.

| | |
|---|---|
| Delay | `time` in seconds, up to two, and sweepable — the line interpolates, so it glides rather than steps. `feedback` is how much comes round again |
| Reverb | four feedback combs into two allpasses. `size` stretches every delay together, `decay` sets how long the tail lasts |

Everything else here is a pure function of `(x, y, t)`, which is what lets the
video renderer run rows in parallel. These two are not, so **they only work on
the audio path**: the video program is given no state, a Delay becomes a wire,
and a Reverb dims by one minus its feedback. That asymmetry is the price of
having them at all, and [ADR-0027](docs/adr/0027-delay-lines-give-the-audio-path-a-memory.md)
sets out why it cannot be avoided. For a picture with a past, use `Feedback`.

An oscillator's phase is carried the same way and falls back the same way, but
you will not notice: what it falls back *to* is the multiply it replaced, so the
picture is identical either way.

## Using the editor

| | |
|---|---|
| Add a module | click it in the left palette |
| Patch | drag from one socket to another |
| Unplug | drag a connected input away |
| Pan / zoom | drag the background or right-drag / mouse wheel |
| Frame the patch | `F` |
| Delete a module | `Delete` — except the Output, which every patch keeps |
| Set exact values | select a module, use the inspector on the right |
| Undo / redo | `Ctrl+Z` and `Ctrl+Shift+Z`, or `↶` and `↷` on the toolbar. `Ctrl+Y` redoes as well, and `Cmd` does what `Ctrl` does |
| Toolbar | symbols rather than words, and every one of them says what it is if you rest on it. What is done to the patch is on the left, what belongs to the program — the assistant, the settings, the about — on the right |
| Everything else | select the **Output** |

Any input with nothing plugged into it uses the value shown on the node, so most
patches need no constant modules at all.

Undo goes back two hundred edits: a module added, removed or moved, a wire
plugged or pulled, a knob turned, a note edited. A step is the whole patch
rather than an inverse of the edit that made it, so anything that survives being
saved survives being undone and nothing has to be taught what a particular edit
was — which is also why moving a module undoes, having cost nothing to include.

What counts as one step is the one thing it does have an opinion about. A slider
dragged across its range makes an edit a frame and is one press of undo, not a
hundred; unplugging a wire and plugging it in somewhere else is two edits and one
gesture, and goes back in one press as well. Opening a patch or picking a preset
starts again with nothing behind it: undoing into the patch you had before you
opened a file would not be undo, it would be losing the file. What
the assistant makes is the other way about — it is an edit to your patch rather
than a document in place of it, however much of it changes, so it lands in the
history like anything else and one press of undo has your patch back.

A patch that has been edited and not written out says so with a dot beside the
name in the title bar, and anything that would close it asks first — quitting,
opening a file, picking a preset — offering to save it, to discard the changes,
or to stay where you are. The assistant is not among them and does not need to
be: what it does is an edit, so there is nothing there to lose.

Cancelling the save dialog cancels the whole thing rather than the save alone:
somebody who asked to save and then thought better of where has not agreed to
lose the patch. Undoing back to what you started with settles it again, for the
same reason a step is the whole document — what is compared is the patch, not
whether anybody typed.

The Output's panel is where the instrument is set rather than the patch: how
large the preview is and whether the processor or the GPU draws it, whether
sound is playing, and the four ways of getting a picture or a sound out of the
program. None of it is saved with the patch — a preview size belongs to the
machine you are working at — but it sits with the block it acts on rather than
along a toolbar.

| | |
|---|---|
| Picture | **Size** and **Render** (GPU or processor) |
| Sound | **Audio on** |
| Timeline | **Rewind**, which takes the picture and the sound back to zero together |
| Export | **Length** in seconds, and **Export…** |

The status bar carries whatever the compiler wants to say about the patch as it
stands, in amber. Most of it is about the patches that compile perfectly and do
nothing: an oscillator with nothing driving its `in`, or an output with nothing
wired into it at all — one flat colour on the screen, silence at the speakers.

## Layout

| Project | |
|---|---|
| `src/Flyback.Core` | graph model, compiler, renderer, PNG/WAV/JPEG/AVI writers — no UI dependency |
| `src/Flyback.App` | Avalonia editor and live preview, built in C# without XAML — the window is one class across a file per region ([ADR-0039](docs/adr/0039-one-window-class-across-a-file-per-region.md)) |
| `src/Flyback.Plugins` | the plugin contract and the loader — no dependencies either |
| `src/Flyback.Plugins.Wasapi` | Windows sound output, via NAudio |
| `src/Flyback.Plugins.CoreAudio` | macOS sound output, straight to the default output audio unit |
| `src/Flyback.Plugins.Alsa` | Linux sound output, through libasound's default device |
| `src/Flyback.Plugins.Supersaw` | the Supersaw oscillator, as a module plugin |
| `src/Flyback.Plugins.Space` | delay and reverb — the only modules with a memory of their own |

Why it is built this way is recorded in [docs/adr](docs/adr) — 39 decision
records covering the compiler, the renderer, the shell and the boundaries
between them.

## Tests

```bash
dotnet test Flyback.slnx
```

| Project | |
|---|---|
| `tests/Flyback.Core.Specs` | Gherkin scenarios (Reqnroll) for the behaviour the ADRs specify — dead code, cycles, port typing, input defaults, feedback, guarded arithmetic, coordinate conventions |
| `tests/Flyback.Core.Tests` | snapshot tests of the rendered presets, plus property and fuzz tests over the compiler and interpreter |
| `tests/Flyback.Plugins.Tests` | loads the real shipped plugins off disk — discovery, which backend wins on this machine, and that a type crossing the boundary keeps one identity |
| `tests/Flyback.App.Tests` | the shell: what an edit during playback does to the sound the device is already being handed, and the controls themselves — laid out, clicked and dragged through Avalonia's headless platform |

Each `.feature` file names the ADR it pins, so the records and the specs stay
honest about each other.

Snapshots compare decoded pixels rather than file bytes, because PNG compression
and `MathF` results can both shift without any behaviour changing. When output
legitimately changes, inspect the `.received.png` next to its `.verified.png`
baseline and rename it to approve.

The fuzzer generates random well-formed patches and pushes them through compile
and render. It is the only test that reaches all 56 modules, and it is what
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

Patches save as JSON (`.fbk`).

**Export…** writes the thing the instrument actually makes, and there is one
button for all of it. Set the length in seconds in the same panel — it is the
one parameter of a moving export that cannot be defaulted, since a patch is an
endless function of `(x, y, t)`. The frame is whatever **Size** says, a few rows
above it.

The file name decides what you get, and the dialog offers only the kinds this
patch has: **AVI** for the moving picture with the sound alongside it, **PNG**
for one frame of it, and **WAV** for the sound on its own. Wire nothing into
`colour` and it offers neither picture; wire nothing into `left` or `right` and
it offers no WAV. A patch that reaches neither says so instead of opening a
dialog that could only produce a file of nothing.

A PNG is the odd one of the three. It ignores the length, being a single frame
at the moment on screen, and it is written at 1920×1080 whatever **Size** says —
the preview's resolution is a matter of keeping up, and a still has nothing to
keep up with.

The video file is an AVI: every frame an independent JPEG, 16-bit PCM interleaved
alongside. Both encoders are written here beside the PNG and WAV ones, so export
needs nothing installed and works headlessly. A patch with nothing wired into the
Output's `left` or `right` gets a video-only file rather than a silent track.

| | |
|---|---|
| Rate | 30 frames a second |
| Size | 1.5 to 2.5 MB a second at 960×540 depending on the patch, and AVI stops at 4 GB — half an hour or so |
| Cost | the picture is rendered on the processor even when the preview is on the GPU, so an expensive patch takes longer to write than to watch |
| Stopping | the Export button becomes **Stop**, and stopping keeps what was rendered as a shorter video rather than a broken one |

Feedback works in a moving export and does not in a still: one renderer runs the
whole clip, so each frame reads the one before it exactly as on screen. The sound
in it is byte-for-byte the WAV the same patch writes on its own — one oscillator
phase and one delay tail, running the length of the clip.

MJPEG is an old compression and every frame pays full price, which is the cost of
a container simple enough to write by hand under
[ADR-0019](docs/adr/0019-no-third-party-dependencies-in-the-engine.md). Anything
wanting an MP4 can transcode one; this is a file every tool accepts as input.
