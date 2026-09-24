# Flyback engineering guide

How the codebase is put together, how its code is written, and how its tests are
written. The decisions themselves are in [`adr/`](adr/README.md); this is the map
that says which of them you are standing on. [CONTRIBUTING.md](../CONTRIBUTING.md)
is what a change has to carry, and [language.md](language.md) is the text
language.

Where this guide and an ADR disagree, the ADR is right and this guide is stale.
Where an ADR and the code disagree, that is a finding worth raising.

- [1. The shape](#1-the-shape)
- [2. The patch model](#2-the-patch-model)
- [3. The compiler](#3-the-compiler)
- [4. Three backends, one specification](#4-three-backends-one-specification)
- [5. Picture and sound](#5-picture-and-sound)
- [6. Threads](#6-threads)
- [7. Files and the text language](#7-files-and-the-text-language)
- [8. Plugins](#8-plugins)
- [9. The shells](#9-the-shells)
- [10. Build, gate and release](#10-build-gate-and-release)
- [11. Code style](#11-code-style)
- [12. Writing tests](#12-writing-tests)
- [13. Recipes](#13-recipes)
- [14. Refactors that are already declined](#14-refactors-that-are-already-declined)

---

## 1. The shape

One module graph makes a picture and a sound. A patch is compiled to a flat
program for a register machine, once for the screen and once for the speakers,
and everything else in the repository either builds that graph, runs that
program, or shows the result.

```text
Flyback.Core      the patch model, the module catalog, the opcodes, the Emitter
   ^              what a plugin is compiled against; references nothing
Flyback.Engine    the compiler, the three backends, the renderers, the language, file I/O
   ^              free to change between releases; no third-party packages
Flyback.Plugins   the plugin contract and the host that loads plugins off disk
   ^
Flyback.Ui        what two shells draw with: preview, sound device, colors, settings
   ^
Flyback.App       the editor (Avalonia)          Flyback.exe
Flyback.Viewer    plays a patch, writes nothing  flyback-viewer.exe
Flyback.Cli       render, check, print, pack     flyback-cli.exe   (no Ui, no Avalonia)

Flyback.Plugins.* twelve plugins, built into plugins/<Name>/ and loaded at run time
```

References run one way, upward in that drawing, and there is no abstraction
layer between the layers: a shell calls the engine's concrete types
([0002](adr/0002-split-engine-from-shell.md)).

| Project | Holds | Rule it lives under |
|---|---|---|
| `Flyback.Core` | `Patch`, `NodeDef`, `PortSpec`, `NodeCatalog`, presets, `OpCode`, `Emitter` | The plugin-facing surface. Its public API is versioned separately ([0102](adr/0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)). |
| `Flyback.Engine` | `PatchCompiler`, `CompiledPatch`, IL and GLSL backends, `SynthRenderer`, `AudioRenderer`, codecs, `Language/`, `PatchIO` | No third-party dependencies ([0019](adr/0019-no-third-party-dependencies-in-the-engine.md)). PNG, JPEG, WAV and AVI are written by hand for that reason. |
| `Flyback.Plugins` | `IFlybackPlugin`, `IPluginRegistry`, the device, MIDI, secret and assistant interfaces, `PluginHost`, `PatchWorkbench` | References Engine with `PrivateAssets="all"`, so a plugin cannot reach the engine through it. |
| `Flyback.Ui` | `PreviewHost`, the CPU and GPU preview surfaces, `AudioEngine`, `Colors`, `Text`, `OutputSettings` | Exists so the viewer shares the editor's preview without referencing the editor ([0124](adr/0124-what-two-shells-draw-with-is-a-project-of-its-own.md)). |
| `Flyback.App` | `MainWindow`, `NodeEditor`, inspector, assistant panel, recording, updates, usage counts | UI is C# with no XAML ([0016](adr/0016-build-the-ui-in-c-sharp-without-xaml.md)). |
| `Flyback.Viewer` | A window, a transport and an argument parser | Writes nothing to disk ([0123](adr/0123-a-third-program-plays-a-patch-and-writes-nothing.md)). |
| `Flyback.Cli` | One file per command over `System.CommandLine` | The only place deterministic export lives ([0078](adr/0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)). |

Two namespace quirks are deliberate. `Flyback.Engine` declares
`RootNamespace=Flyback.Core`, and `Flyback.Ui` declares `RootNamespace=Flyback.App`:
both projects were split out of their parent, and keeping the namespaces meant no
moved file needed an edit. Find a type by project, not by namespace.

---

## 2. The patch model

All of it is in `src/Flyback.Core/Graph`.

- **`Patch`** is nodes, connections, groups, panel controls and a format version.
  It has exactly one Output ([0037](adr/0037-one-output-block-that-every-patch-has.md)):
  `CanAdd` refuses a second, `Remove` refuses the one.
- **`NodeInstance`** is an id, a `TypeId` string, a position, a name, an `Off`
  switch, `InputValues` (one float per input, the knob) and a `State` dictionary
  of JSON for whatever else the module carries
  ([0061](adr/0061-what-a-module-carries-is-kept-in-one-store.md)).
- **`Connection`** is its two ends. A wire has no id.
- **`NodeDef`** is a module: type id, name, category, input and output
  `PortSpec`s, an emit function and a description. Modules are data in one
  catalog, not classes ([0008](adr/0008-modules-as-data-in-one-catalogue.md)).
- **`PortSpec`** is a socket. `PortKind` is `Scalar`, `Color` (three registers)
  or `Any`, and `Any` is what lets one Multiply work on both
  ([0010](adr/0010-any-typed-ports-for-polymorphic-maths.md)). Every input
  carries an editable default ([0009](adr/0009-editable-defaults-on-every-input.md)),
  and may be normalled to a hidden module such as `time` with no wire
  ([0050](adr/0050-normalled-sockets-carry-a-signal-with-no-wire.md)).

A module is declared like this, in one of the `NodeCatalog.*.cs` partials:

```csharp
yield return new NodeDef(
    "osc.pulse", "Pulse", ModuleCategories.Oscillators,
    [
        Domain("in"), Freq, Num("phase", 0f, 0f, 1f), Num("width", 0.5f, 0f, 1f),
        Num("amp", 1f, 0f, 2f), Num("bias", 0f, -2f, 2f)
    ],
    [Num("out")],
    (em, i) =>
    {
        var phase = em.Phase(i[0], i[1], i[2]);
        var wave = em.Add(em.Mul(em.Binary(OpCode.Step, i[3], em.Unary(OpCode.Fract, phase)), 2f), -1f);
        return [em.Add(em.Mul(wave, i[4]), i[5])];
    },
    "A square with an adjustable duty cycle.");
```

The emit function receives the `Emitter` and its resolved inputs as `Slot`s and
returns one `Slot` per output. It writes ops; it does not compute anything. That
is the whole extension mechanism, and a plugin uses the same one.

`NodeCatalog` is the built-in set, assembled once in a static constructor from
the partials. `ModuleCatalog` is the immutable set a running program has: built-ins
plus whatever plugins added. `NodeCatalog.Install` is called once at startup,
before any window exists. `ModuleCategories.All` is both the set of curated
categories and the palette order.

Presets are C# against `PatchBuilder` (`b.Add(typeId, (port, value)…)`, then
chained `b.Wire(from, port, to, port)`). They declare no coordinates
([0070](adr/0070-a-preset-declares-no-coordinates.md)) and arrive with their
arithmetic folded into Expressions
([0108](adr/0108-a-preset-arrives-with-its-arithmetic-folded.md)): `Presets.All`
runs `ExpressionFusion.Fuse` and `PatchLayout.Arrange` over each. The
`authoring-presets` skill covers writing one.

---

## 3. The compiler

`src/Flyback.Engine/Compile/PatchCompiler.cs`. Three public roots,
`CompileForVideo`, `CompileForAudio` and `CompileForProbe`, funnel into one
`Compile`. The result is a `CompiledPatch` and a list of `CompileIssue`s.
**Problems are values, not exceptions**, here and almost everywhere else.

**Backwards from the Output** ([0011](adr/0011-compile-backwards-from-output.md)).
`Resolve(node)` is a memoized recursive walk up the wires from the sink. Only the
sink's ports for this program are resolved, the screen's `color` or the speakers'
`left`, `right` and `volume`, so the other half's upstream is never visited. That
is how one graph becomes two programs ([0022](adr/0022-audio-and-video-are-two-sinks-over-one-patch.md)).

**A flat register machine** ([0005](adr/0005-compile-to-a-flat-register-machine.md)).
An `Op` is `(Code, Out, A, B, C, K)`. The `Emitter` hands out a fresh register
per value, so the program is in SSA form; a `Slot` is a base register and a width
of 1 or 3, and a scalar broadcasts across a color
([0007](adr/0007-register-slots-with-scalar-broadcast.md)). Registers are
`double` ([0032](adr/0032-the-registers-are-double-precision.md)). Constants and
loads are shared through the emitter's dictionaries.

**47 opcodes**, in `src/Flyback.Core/Compile/OpCode.cs`, numbered by hand and
never renumbered: the number is what a plugin bakes in. `OpShape` is the table of
each opcode's inputs, outputs and whether it must be kept.

**Unread ops are swept** ([0096](adr/0096-an-op-nothing-reads-is-left-out.md)).
`Emitter.ToProgram` makes one backward pass and drops any op whose outputs nothing
reads, except the ones `OpShape.Kept` names: ops with no output, and `Delay`,
`Allpass` and `Phase`, which own memory by position.

**A pixel runs only what a pixel changes**
([0064](adr/0064-a-pixel-runs-only-what-a-pixel-changes.md)). `FramePlan` gives
each op the latest stage any of its inputs needs, `Frame`, `Row` or `Pixel`, and
sorts the program into three runs. Most of a large patch lands in `Frame` and
runs once.

**A cycle carries its own delay** ([0075](adr/0075-a-cycle-carries-its-own-delay.md)).
`Cycles.Backwards` finds the wire that closes each loop. That wire is read from a
plane cell rather than followed, and every write is emitted at the end of the
program, so each read precedes each write and the latency is one evaluation.

**State survives a rebuild** ([0067](adr/0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)).
The emitter records which node owns each delay line, phase, unit cell and plane
in `StateOwners`, and `DelayState.Adopt` carries the old contents into the new
program per owner. The whole patch is recompiled on every edit
([0021](adr/0021-recompile-the-whole-patch-on-every-edit.md)), and this is what
makes that inaudible.

**Knobs recompile nothing** ([0086](adr/0086-panel-knobs-are-read-as-live-values.md)).
A panel knob, a MIDI voice and a meter reading arrive as `LiveValues`, a
name-keyed `float[]` the program reads with a `Live` op. A single float is atomic
and the block is not, deliberately: the audio thread takes no lock.

A Probe is a second compile root ([0040](adr/0040-a-probe-is-a-second-compile-root.md)),
a Scan is a Probe read backwards ([0043](adr/0043-a-scan-is-a-probe-read-backwards.md)),
and both work by pushing a substitute domain onto the emitter before resolving a
`Swept` input.

---

## 4. Three backends, one specification

| Backend | Where | Runs |
|---|---|---|
| Interpreter | `CompiledPatch.Evaluate` | Everything, always. **It is the specification.** |
| IL | `IlEmitter`, `IlOps`, `IlProgram`, `IlCompiler` | The CPU picture and the sound, once built, and every offline render ([0076](adr/0076-the-processor-runs-a-program-as-il-once-it-is-built.md)) |
| GLSL | `GlslEmitter` | The live preview and live recording ([0035](adr/0035-a-glsl-backend-for-the-video-path.md)) |

**The interpreter** is one `switch` over the flat op array. Register access has no
bounds check; `Vouch` checks the whole program once in the constructor instead.
Arithmetic is guarded rather than allowed to produce NaN
([0013](adr/0013-guard-arithmetic-instead-of-propagating-nan.md)): divide by zero
is 0, the root of a negative is 0, `Guard` turns a non-finite value into 0.

**The IL backend** is built on a background thread at below-normal priority and
attached to the program through a `Volatile` field that renderers read once per
frame or buffer. Each op is one inlined method in `IlOps` that calls the
interpreter's own helpers, so a guard exists once. A stretch longer than 256 ops
is several methods called in order, because past a few thousand locals the JIT
stops optimizing a method and every op becomes a call. Every build is run against the
interpreter before it is trusted, bit for bit, and refused if it differs.
`--interpreted` keeps a run, or a `flyback-cli render`, on the interpreter.

**The GLSL backend** emits text and touches no GL. Where a GLSL builtin disagrees
with the interpreter (`fract`, `mod`, `mix`, `pow`, `smoothstep`, `atan`) it emits
a helper that transcribes the interpreter instead. This is why the three opcode
switches are not unified: the transcription is the point.

What keeps them in agreement is tests, all in `tests/Flyback.Core.Tests/Compile`,
all theories over `Enum.GetValues<OpCode>()`:

- `TotalityTests`: every op, fed 21 hostile values (±0, ±∞, NaN, denormals),
  neither throws nor differs between IL and interpreter.
- `IlProgramTests`: IL equals the interpreter over every preset, staged and whole.
- `GlslEmitterTests.Every_opcode_lowers_to_a_line`: every op produces a line,
  except four that are named by hand as producing none.
- `GlslEmitterTests.Preset_lowers_as_approved`: a snapshot of every preset's
  shader in both dialects.

A new opcode fails all of these until it exists everywhere.

---

## 5. Picture and sound

**Picture.** `SynthRenderer` runs the program per pixel with `Parallel.For` over
rows, one register bank per worker, on every core but one
([0006](adr/0006-scalar-interpreter-parallel-over-rows.md)). The spare core is
for the audio callback. Coordinates and value ranges are fixed by
[0014](adr/0014-coordinate-and-value-conventions.md). Feedback reads the previous
frame, which ping-pongs between two buffers. `PlaneState` holds a float per pixel
per plane for state on the video path, laid out pixel-major so rows stay
independent ([0074](adr/0074-a-cell-is-a-plane-on-the-video-path.md)).

**Sound.** `AudioRenderer` is single-threaded by design and allocates nothing once
built. It runs the program four times per output sample and decimates through a
64-tap windowed sinc, then a DC blocker and a clamp
([0023](adr/0023-oversample-the-audio-path.md)). Oscillators accumulate phase on
this path rather than multiplying time
([0030](adr/0030-oscillators-accumulate-their-phase.md)). `x` and `y` are pinned
to 0; the picture is heard only through a Scan
([0077](adr/0077-the-picture-is-heard-only-through-a-scan.md)).

**Memory.** `DelayState` is everything a sound program remembers: delay and
allpass rings, phases, unit cells, one plane cell per cycle and the trace rings a
Scope reads. It belongs to the renderer, not the program, and stateful ops index
it by their position among ops of their kind. With no `DelayState` each stateful
op degrades to something total (`Delay` passes through), which is how the video
path runs the same ops.

**The device is not the engine's** ([0024](adr/0024-audio-device-in-the-shell.md)).
The engine generates samples. `Flyback.Ui/Audio/AudioEngine` joins a program to an
`IAudioDevice` that a platform plugin supplies, and its callback swaps one
immutable `State` record with `Volatile` rather than locking. It is the master
clock the preview follows.

**Time is seconds** ([0048](adr/0048-time-is-seconds-and-nothing-else.md)), and
nothing in the engine reads a wall clock. `t` is an argument.

---

## 6. Threads

| Thread | Does | Must not |
|---|---|---|
| UI (Avalonia dispatcher) | Editing, compiling, layout | Render a frame ([0018](adr/0018-never-render-frames-on-the-ui-thread.md)). Blocking the dispatcher on a `Parallel.For` deadlocks against the compositor. |
| Thread pool | CPU preview frames, one at a time behind a `rendering` flag | Overlap: `SynthRenderer` is not thread-safe. |
| Compositor render thread | `GpuPreviewSurface.OnOpenGlRender` | Block the dispatcher; hand-off is behind one `gate` lock. |
| Audio callback | `AudioRenderer` into the device buffer | Lock, allocate or wait. |
| MIDI driver | Notes into `LiveValues` through `MidiHub` | Open or close a device with the hub's lock held. |
| `Flyback IL compiler` | Builds IL at below-normal priority | Take the processor from the two above. |

The capture sinks (`IFrameSink`, `IAudioSink`) state the rule for anything on a
hot path: it may copy, and it must not encode, allocate, lock or wait.

Plugins are constructed on the UI thread at startup. `Register` has to be cheap
and must not touch a device.

---

## 7. Files and the text language

| Extension | Is | Code |
|---|---|---|
| `.fbk` | The patch as indented JSON, keyed by string type ids ([0020](adr/0020-json-patch-files-keyed-by-string-type-ids.md)) | `PatchIO` |
| `.fbkb` | A zip: `patch.fbk`, the samples and pictures it names under `files/`, optionally the conversation ([0060](adr/0060-a-bundle-is-a-patch-and-what-it-names.md)) | `PatchBundle` |
| `.fbks` | The patch as source text ([0065](adr/0065-a-text-language-that-parses-to-a-patch.md)) | `Language/` |

`PatchFile.Open` is the one door for all three, and both shells and the CLI go
through it. A patch names its samples and pictures rather than carrying them
([0052](adr/0052-a-patch-names-its-samples-rather-than-carrying-them.md)); the
compiler is handed an `ISampleLibrary` and never does file I/O itself.

A file records which plugin each foreign module came from
([0026](adr/0026-modules-from-plugins-with-provenance-in-the-file.md)), so
`PatchLoad` can say what is missing instead of failing. A file from a newer
format version is refused whole. Adding a module is not a version bump.

The language pipeline is `Lexer.Scan → Lexer.Statements → Parser.Parse →
Binder.Build`. The parser knows nothing about modules; the binder reads short
names, socket names and arities off the running `ModuleCatalog`, so a plugin's
modules are usable in text the moment it loads. It never throws: faults are
`LanguageIssue(Line, Column, Message)`. Building is exact. Printing
(`PatchPrinter`) is lossy by design, and its guarantee is narrower and testable:
print a patch, build the text, and you get the same program opcode for opcode.
[language.md](language.md) is the reference and
[language-for-agents.md](language-for-agents.md) is the short form an assistant
is briefed with.

Copy and paste move a patch as the same JSON
([0045](adr/0045-what-is-copied-is-a-patch-file.md)), and undo is whole-document
snapshots in `PatchHistory`.

---

## 8. Plugins

A plugin is one public parameterless class implementing `IFlybackPlugin`, in an
assembly built with `EnableDynamicLoading`, in its own folder under `plugins/`
([0025](adr/0025-platform-io-behind-loadable-plugins.md)). `Register` calls the
registry:

```csharp
void AddAudioOutput(IAudioOutput output);
void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules);
void AddPresets(IReadOnlyList<PatchPreset> presets);
void AddPatchAssistant(IPatchAssistant assistant);
void AddSecretStore(ISecretStore store);
void AddMidiInput(IMidiInput input);
```

A new kind of extension is a new method. Existing plugins only call the
interface, so adding one does not break them.

**Loading.** `PluginHost.Load` scans once at startup and never reloads: code the
audio thread calls into cannot be unloaded safely. Each folder gets its own
`AssemblyLoadContext` with a resolver over the plugin's `.deps.json`, so two
plugins may depend on different versions of a package. `Flyback.Core`,
`Flyback.Engine` and `Flyback.Plugins` always resolve to the host's copy;
otherwise the contract types get a second identity and every cast fails. That is
why every plugin references Core and Plugins with `Private="false"
ExcludeAssets="runtime"`.

**Nothing throws.** A bad folder, a duplicate id or a refused module becomes a
`PluginProblem` in the catalog. First registration wins for every kind, and
folders are read in ordinal order so two runs agree.

**Module refusals** are `ModuleCatalog`'s, in Core. A provider is refused for a
blank id, the reserved built-in id, an id already loaded, or an id that would
claim existing module ids. A module is refused unless its type id starts with
`<provider id>.`, or if it is defined twice.

**A plugin declares its modules**
([0134](adr/0134-a-plugin-declares-its-modules-and-is-refused-for-one-it-did-not.md))
with `[assembly: FlybackModule(id, name)]`, which the install dialog and the
shared plugins site read from metadata. After `Register`, a plugin compiled
against contract 1.2 or later that registered a module it did not declare is
rolled back whole and becomes a `PluginProblem`. `pack-plugin` runs the same load
before writing a package.

**The contract has its own version**
([0102](adr/0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)).
`PluginContractVersion` in `Directory.Build.props` is the `AssemblyVersion` of
Core and Plugins. The host compares it with what the plugin was compiled against
before it looks at a single type: an older major needs rebuilding, a newer major
or minor needs a newer Flyback. `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt` beside both projects are enforced by an analyzer, so
neither project builds until a surface change is written down. The first
`*REMOVED*` line since a release moves the major; the first added line moves the
minor.

**Building.** Plugins are not project references. A host project lists
`PluginProject` items and `Directory.Build.targets` builds each one straight into
`plugins/<FolderName>/`, portable, with an optional `Platform` of `win`, `osx` or
`linux` ([0028](adr/0028-publish-one-platform-at-a-time.md)). Only `Flyback.App`
and `Flyback.Plugins.Tests` declare them. The CLI and the viewer publish into the
app's folder and read what it laid out; declaring plugins there would clear the
folder out from under it.

**What ships.**

| Plugin | Adds | Third-party |
|---|---|---|
| WinIO, MacIO, LinuxIO | Sound and MIDI per platform ([0063](adr/0063-one-plugin-per-platform-for-sound-and-midi.md)) | `NAudio.Wasapi` in WinIO; the others are hand-written P/Invoke |
| Dpapi, Keychain, Keyring | Where an API key is kept ([0034](adr/0034-settings-in-a-file-the-key-in-the-operating-system.md)) | `ProtectedData` in Dpapi |
| Picture, Voice, Effects, Mastering | Modules and presets | none |
| OpenAi, Gemini | Patch assistants ([0033](adr/0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md), [0066](adr/0066-a-second-wire-format-so-one-model-can-hear.md)) | none: hand-written JSON over HTTP |

An assistant never touches the patch directly. Everything it does goes through
`PatchWorkbench`, which is the tool surface, the limits and the senses (looking
at a frame, hearing a clip). The two session loops in `OpenAiSession` and
`GeminiSession` are similar on purpose and stay separate; the reason is in
`GeminiSession.cs`'s header.

`tests/Flyback.Plugins.Sample` is the worked example of a module plugin, and
[`site/plugins.html`](../site/plugins.html) is the published guide.

---

## 9. The shells

### The editor

**No XAML** ([0016](adr/0016-build-the-ui-in-c-sharp-without-xaml.md)). Controls
are built in C#. The shared values a style sheet would have held are in two token
files in `Flyback.Ui/Controls`: `Colors.cs` (colors by role: `Window`, `Canvas`,
`Panel`, `Node`…) and `Text.cs` (eight type sizes from `Micro` to `Display`, and
the muted brush). A color or a font size written as a literal in a control is
drift. The site's CSS mirrors `Colors.cs`.

**Two hubs and the regions around them**
([0148](adr/0148-the-window-is-two-hubs-and-the-regions-around-them.md)).
`Document` owns who owns the patch, the write-back into the text and where an
undo lands; `Playback` owns compiling, the sound device, pause and mute. A region is a
class that takes the hubs and the shared things it reads (the canvas, the
preview, the plugins), owns its own fields and raises events. `MainWindow` builds
them and lays them out. Regions not yet moved are still `partial` files of the
window. There are no view models, and that has been decided twice.

**The node editor is one control**
([0017](adr/0017-draw-the-node-editor-in-one-control.md)). `NodeEditor` overrides
`Render` and the pointer handlers, and nothing inside it is a control. It is
split the same way (`Painting`, `Interaction`, `HitTesting`, `Groups`,
`Clipboard`…). `NodeGeometry` is the single source of measurement that both
painting and hit-testing call, which is what keeps a socket where it is drawn. A
module is drawn as its category, or as itself if it is a standout or a plugin
paints it ([0116](adr/0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md),
[0118](adr/0118-a-plugin-paints-its-own-module-background.md)).

**The preview.** `PreviewHost` holds whichever backend is live. It starts on the
GPU and falls back to the CPU once per session with no way back.
`GpuFrameRenderer` owns the shader programs and the feedback texture pair and
returns error strings rather than throwing. A live recording takes the GPU frame
through `GpuReadback` ([0049](adr/0049-record-the-gpu-frame-not-the-interpreter.md))
and encodes with ffmpeg where it is found, Motion JPEG AVI where it is not
([0089](adr/0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md)).

**Startup** is in `Startup.Load`: tidy a pending update, `PluginHost.Load()`,
`NodeCatalog.Install`, then the first window. The catalog is final before any
palette is built.

**What it writes**, all JSON under `%APPDATA%/Flyback`
(`GlobalConstants.DataFolder`): `output.json`, `canvas.json`, `layout.json`,
`update.json`, `usage.json`, and the `recovery/`, `updates/`, `sessions/`,
`groups/` and `presets/` folders. API keys go to the operating system's store,
never to a file.

- **Recovery** ([0103](adr/0103-unsaved-work-outlives-a-crash.md)): each window
  writes a snapshot and holds a `.lock` open beside it. A snapshot whose lock can
  be taken belongs to nobody. That is the whole crash detection.
- **Updates** ([0088](adr/0088-a-release-installs-itself-at-the-next-start.md)):
  a release is installed at the next start only if `SHA256SUMS.sig` verifies
  against the committed public key. The new version installs itself through
  `--apply-update`, and that argument contract is only ever added to.
- **Usage** ([0094](adr/0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md)):
  `Statistics/Usage.cs` is the entire policy of what is counted and what is
  refused.

### The viewer and the CLI

Both parse with `System.CommandLine` and both load plugins and install the
catalog before building their command tree. The viewer is `ViewerPlayer` (the
transport, with no window type in it) and `ViewerWindow`; `--hidden` runs the
player with no preview at all. The CLI declares each command's options in
`Program.cs` and runs each in its own file (`RenderCommand`, `CheckCommand`…).
Commands take two `TextWriter`s and return an exit code, which is what makes them
testable without a process. `flyback-cli viewer` parses nothing and starts
`flyback-viewer` beside it.

---

## 10. Build, gate and release

Four short, commented files decide how everything here builds:
`Directory.Build.props` for what every project is compiled with,
`Directory.Packages.props` for every package version, `.editorconfig` for the
analyzer rules this codebase answers differently, and `global.json` for the test
runner. The reason for anything surprising in them is written above it.

```bash
dotnet test --solution Flyback.slnx -c Release
```

```bash
docker build --target gate .
```

The second is the truth ([0120](adr/0120-every-change-passes-the-gate-a-release-passes.md)).
The first runs against whatever the machine happens to have, and the tests that
need something missing skip rather than fail — the headless UI tests want a font
stack to rasterize with, the recording tests want ffmpeg on `PATH` — so a local
run can be green about code it never ran. The Dockerfile carries both, which is
why CI builds it rather than installing an SDK.

The stages stack, and that is what stops a release skipping anything. `publish`
builds on `gate`, so per-platform artifacts cannot exist without every test
having passed. `measured` builds on `gate` too and runs the tests again under
coverage, weekly rather than per change; nothing else reads it. `coverage.sh`
builds it and writes the table, for the Coverage workflow and locally alike, as
`release.sh` is the whole release for the Release workflow. The gate's
restore is locked to the committed lock files, so a version that moved fails
there rather than building.

`pages.yml` publishes `site/` unbuilt on every push to `main` that touches it.
There is no build step to catch a stale sentence, which is why a site edit goes
in the same commit as the change it describes.

A release passes `-p:Version=`; every other build reports a suffixed version,
which is what keeps it from updating itself or being counted. What the Release
workflow does with that is its own header, and
[0088](adr/0088-a-release-installs-itself-at-the-next-start.md) is why an update
is signed at all.

---

## 11. Code style

There is no formatter configuration. The style is what the code already does, and
new code should be indistinguishable from the file it lands in.

### Language

- File-scoped namespaces, in every file.
- `sealed` on a class by default. Static classes for things with no instance.
- Records for data, `readonly record struct` for small values
  (`Slot`, `PortNormal`, `AudioFormat`, `MidiMessage`). Primary constructors where
  a type is its parameters.
- `var` for locals. Collection expressions (`[]`, `[.. xs]`). Switch expressions.
  `is null` and `is not null`, never `== null`.
- Private fields are `camelCase` with no underscore, and `private` is written out.
  Constants are `PascalCase`. Native constants keep their native names
  (`FLASHW_TRAY`).
- `internal` is the default visibility outside Core's and Plugins' deliberate
  public surface. Tests reach internals through `InternalsVisibleTo`, not by
  making things public.
- No `#region`, no `#pragma warning disable`, no `TODO`. A warning is an error, so
  it is fixed rather than silenced.
- `unsafe`, `Span<T>` and `Unsafe.Add` appear where a hot loop has earned them,
  with the check that makes them safe done once up front (`Vouch`).
- Nullable reference types are on. A `!` is a claim that needs to be true.

### Design habits

- **A failure is a value.** `CompileIssue`, `LanguageIssue`, `PluginProblem`,
  `PatchLoad`, `ModuleAddition.Rejected`, `BundleReport`. Code that reads a file,
  loads a plugin or compiles a patch reports; it does not throw. Exceptions are
  for broken invariants (`Patch.Output` on a patch with none).
- **Data over types.** A module is a `NodeDef` value. What a module carries is a
  part (`NodeExtra`), not a subtype ([0054](adr/0054-what-a-module-carries-is-a-part-not-a-subtype.md)).
  A plugin's settings and its module's editor are declared as fields
  (`SettingField`, `ExtraField`), not drawn by the plugin.
- **Immutable, then swapped.** `ModuleCatalog`, `CompiledPatch` and
  `AudioEngine.State` are replaced whole with a `Volatile` write. That is the
  project's answer to locking on a hot path.
- **Time is an argument.** No `TimeProvider`, no injected clock. Code that needs
  the time is passed seconds.
- **No dependency where a page of code will do.** The engine has none. The
  assistants talk JSON over `HttpClient` by hand rather than take an SDK. A
  first-party build-time analyzer with `PrivateAssets="all"` is fine; ADR-0019 is
  about what ships.
- **One place knows a thing.** `NodeGeometry` for where a socket is, `OpShape` for
  an opcode's arity, `PatchFile.Open` for opening a file, `Colors` and `Text` for
  the look.
- **No interface without a second implementation.** Interfaces exist at the plugin
  boundary and almost nowhere else. A shell calls the engine's concrete types.
- **Split a big class by region, not by pattern.** A large partial class with a
  file per concern is the house shape (`MainWindow`, `NodeEditor`, `NodeCatalog`).

### Drivable by an agent

Most features here are built, tested and debugged by an agent with no eyes on
the window, so every feature needs a way in that works without one. A feature
that only a person watching the screen can check is not finished.

- **A command before a window.** Whatever a patch does can be asked of
  `flyback-cli` or `flyback-viewer`: `check`, `info`, `print --check`,
  `render --at` for a still, `compare` for "is this still the same instrument",
  `flyback-viewer --hidden --for` to hear it. A new question an agent keeps
  answering with a throwaway test gets a command or a flag instead.
- **Answers a script can read.** Exit codes mean one thing each (`Exit`), a
  report has `--json`, and stdout carries only the answer.
- **The editor headless.** A UI feature is reachable from a `UiTest` and, where
  it has a look, from `PatchShotTests`. Screen coordinates and `SendKeys` are the
  last resort (`running-the-app.md`), not the test.
- **Say where it went wrong.** A failure names the module, the socket, the second
  or the frame, the way `compare` says where two patches part, so the next step
  is a fix rather than a bisect.

### Comments

Doc comments are the norm, and they explain why a thing is the way it is, since
what it does is in the code. Density runs 30 to 65 percent of a file, so a file's
line count overstates its code.

- One `<summary>` line where that carries it. `<remarks>` only for a real
  surprise. `<param>` on records, since the parameters are the type.
- **Never narrate history.** Write as if the current shape were the only one it
  ever had. No "now", "no longer", "three rather than six", "unchanged by the
  consolidation". History is in git and the ADRs.
- A comment that is growing into an argument is an ADR.
- American spelling everywhere: "color", "analyzer", "behavior". The identifiers
  already use it, so anything else makes a comment disagree with its code.
- `.csproj`, `.props` and `.targets` files are commented the same way. If a line
  of MSBuild is not obvious, the reason is written above it.

### Names

Domain words, from the instrument rather than the framework: module, socket,
wire, patch, knob, sink, normalled, take, plane, cell. [glossary.md](glossary.md)
has every one, the code name it hides behind and the words it must not be
called. Test and ADR titles are
sentences. A module's type id is `category.name` for built-ins (`osc.sine`,
`math.add`) and `<provider id>.name` for a plugin's, and once shipped it is never
changed: saved patches name it.

### Commits, changelog, ADRs

- Commit straight to `main`; history is linear. The subject is a declarative
  sentence stating what is now true: "The command line can start the player". No
  Conventional Commits prefix. The body is a paragraph on what was wrong and what
  happens now.
- An upgrade, a retarget and a slow-test fix each get a commit of their own.
  Churn that has not been pushed is squashed before it is.
- `CHANGELOG.md` is for what a user would notice: one terse bullet per feature,
  nothing for tests, comments, ADRs or the README.
- An ADR is Nygard's format: context, decision, consequences. One younger than a
  day is rewritten in place; an older one gets a dated amendment. Number a new one
  from `main` at commit time, because concurrent sessions have collided.

---

## 12. Writing tests

### The projects

| Project | Tests | Notes |
|---|---|---|
| `Flyback.Core.Tests` | Model, compiler, backends, language, renderers | The only user of Verify (snapshots) and CsCheck (properties) |
| `Flyback.Specs` | Every feature's requirement as Gherkin scenarios | Reqnroll; no C# test methods; references every project and plugin |
| `Flyback.App.Tests` | Editor, viewer, audio engine, capture, updates | Headless Avalonia |
| `Flyback.Cli.Tests` | Commands run in-process | |
| `Flyback.Plugins.Tests` | The host, every shipped module and preset | Loads real plugins off disk |
| `Flyback.Plugins.OpenAi.Tests`, `.Gemini.Tests` | Wire translation and sessions | Reference the plugin directly: translation is pure |
| `Flyback.Core.Benchmarks` | BenchmarkDotNet | Not a test project |
| `Flyback.Plugins.Sample`, `.FakeAssistant` | Plugins the tests load | Not test projects |

A test goes in the project that owns the behavior, not the one that is convenient.

### Tools

xunit.v3 on Microsoft.Testing.Platform, with every test project an `Exe`.
`Microsoft.NET.Test.Sdk` is absent on purpose; adding it switches a project back
to VSTest. Assertions are **Shouldly, exclusively**. The only `Assert.` calls in
the repository are `Assert.SkipWhen` and `Assert.SkipUnless`.

`dotnet test --filter` is ignored by this platform. Run the assembly itself:

```bash
./tests/Flyback.Core.Tests/bin/Release/net10.0/Flyback.Core.Tests.exe -class "Flyback.Core.Tests.Graph.MixerTests"
```

```bash
./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -method "*WireDrop*"
```

`-list tests` lists them and `-xml results.xml` writes a time per test.

### Naming

The file, the class and the subject share a name: `MixerTests.cs` holds
`public class MixerTests`. The namespace mirrors the folder. A method is a
sentence with underscores that states the rule, capitalized like a sentence:

```text
Every_opcode_lowers_to_a_line
The_il_is_the_interpreter_on_every_input
A_printed_preset_reads_back_as_the_same_instrument
A_wire_lands_on_a_swept_port_rather_than_the_first_scalar_one
The_json_and_the_prose_agree_about_how_bad_it_is
A_model_that_refuses_a_sound_is_reported_without_one
```

No `Should_`, no `Given_When_Then`, no `Test` suffix. Name the rule, not the bug:
`A_chunk_that_lies_about_its_length_costs_nothing` outlives the hunt that found
it, and `Bug3` does not.

### An engine test

Build a patch with `PatchBuilder`, compile it, evaluate it, assert on a register.
There is no test-only patch DSL; tests use the same builder the presets do.

```csharp
private static float Heard(params (int Port, float Value)[] knobs)
{
    var b = new PatchBuilder(NodeCatalog.BuiltIn);
    var mixer = b.Add(Mixer, 0, 0, knobs);
    var output = b.Add(NodeCatalog.OutputTypeId, 200, 0, (NodeCatalog.OutputVolumePort, 1f));

    b.Wire(mixer, 0, output, NodeCatalog.OutputLeftPort);

    var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
    result.HasErrors.ShouldBeFalse();

    var registers = result.Program.AllocateRegisters();
    result.Program.Evaluate(0d, 0d, 0d, registers, default);

    return (float)registers[result.Program.OutputBase];
}

[Fact]
public void Each_input_arrives_at_the_output_scaled_by_its_own_level()
{
    Heard((In(1), 1f), (Level(1), 0.25f)).ShouldBe(0.25f, 1e-5f);
    Heard((In(2), 1f), (Level(2), 0.5f)).ShouldBe(0.5f, 1e-5f);
}
```

The shape to copy: a private helper named for what it observes (`Heard`, `Seen`,
`Run`), and facts that read as a table of cases. Floats are compared with a
tolerance. Backends are compared bit for bit with
`BitConverter.DoubleToInt64Bits`.

For a claim about every module, opcode or preset, write a `[Theory]` over the real
set (`Enum.GetValues<OpCode>()`, `Presets.All`, `PluginHost.Load().Modules.All`)
rather than a list, so that the next one added is covered without anybody
remembering. Where a theory needs exceptions, name them one by one in a list with
a comment, so that adding one is a decision somebody made. When a theory gathers
many failures, collect them and assert once with the first few in the message.

### A UI test

Derive from `UiTest` and mark **every** method `[AvaloniaFact]` or
`[AvaloniaTheory]`, whether it touches a control or not. A plain `[Fact]` in a
`UiTest` class runs on a pool thread, and disposing from there reaches the
dispatcher from the wrong thread. It throws only when work happens to be queued,
so it shows up as some other test failing later.

```csharp
public class BoxLabelTests : UiTest
{
    [AvaloniaFact]
    public void A_long_socket_label_keeps_its_port()
    {
        var fitted = NodeEditor.Fit(Long + ".out", 170);

        fitted.ShouldEndWith("….out");
        NodeEditor.Fit("filter.cutoff", 170).ShouldBe("filter.cutoff");
    }
}
```

`UiTest` gives you `Show(control)`, `NewMainWindow()`, `Owned(window)`,
`Settle(window)`, `All<T>(visual)` and `Pick(combo, name)`. Open windows through
it. Headless gives the whole assembly one UI thread, so a window left open keeps
its preview, timers and engine on that thread for every test after it; `UiTest`
closes what it opened, newest first, and closes a `MainWindow` without asking
about unsaved work.

Prefer asserting on the smallest thing that holds the behavior. Much of the
editor's logic is reachable as `internal static` methods (`NodeEditor.Fit`,
`NodeEditor.Text`), and a test of one needs no window at all.

The Headless xunit adapter is vendored under `tests/Flyback.App.Tests/Headless`
because the published package does not discover tests on xunit.v3 4.x. The
assembly runs with `ParallelMode.Collections`, since every UI test queues on the
one thread anyway. Each UI test blocks a pool thread while it waits for that
thread, so `PoolHeadroom` raises the pool's minimum well past xunit's slot count;
without it a machine short of memory stalls everything else that needs the pool.

### Doubles

Write a small `private sealed class` in the test file. There is no mocking library
and no shared fakes project.

- A sound card is `LoopbackDevice : IAudioDevice`, which keeps the callback and
  lets the test be the audio thread (`Pump(frames)`).
- An HTTP endpoint is `Canned : HttpMessageHandler`, handing back scripted answers.
- A CLI command is a function of two `StringWriter`s returning an exit code.
- A whole assistant is `Flyback.Plugins.FakeAssistant`, a real plugin that replays
  a script of tool calls through `PatchWorkbench`.
- A temp folder is a field and `IDisposable`:

```csharp
private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-priority-" + Guid.NewGuid().ToString("N"));

public void Dispose()
{
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
}
```

Tests that are about loading (`PluginHostTests`, `ShippedPresetTests`,
`EveryModuleTests`) call the real `PluginHost.Load()` against plugins the build
laid out in the test output. The assertion that guards the boundary is
`The_contract_keeps_one_identity_across_the_boundary`.

### Snapshots

Only in `Flyback.Core.Tests`: every preset's GLSL in both dialects
(`Compile/snapshots/*.verified.glsl`) and every preset's rendered frame
(`Rendering/snapshots/*.verified.png`, compared as decoded pixels). A
`*.received.*` file is test output and is ignored by git; `*.verified.*` is the
committed baseline and must have LF line endings.

To accept a change, read at least one diff and agree with it, then move each
`.received` over its `.verified`. A snapshot change that was not the point of the
commit is a finding, not a chore.

### Specs

`Flyback.Specs` states what a patch author can rely on, as requirements a
reader can check:

```gherkin
Scenario: Turning a tone's frequency while it plays does not click
  Given a 10 Hz sine is playing
  When it has played 0.125 seconds
  And its frequency is turned to 12 Hz
  And it plays on for 0.1 seconds
  Then the sound never clicks
```

Every new feature ships with at least one scenario (see `.claude/rules/tests.md`).
The wiring behind each phrase lives in the steps: `PatchSteps` builds and edits
patches, `ScreenSteps`, `SpeakerSteps` and `CompilerSteps` check the picture, the
sound and what the compiler says, `EditingSteps` saves, opens, writes out, undoes
and pastes, `PresetSteps` checks every shipped preset, `ExportSteps` exports
with `flyback-cli render` and plays through the editor's sound engine, and
`KeyboardSteps` plays a stand-in keyboard through the editor's MIDI hub. They share a fresh
`PatchContext` and `Session` per scenario.
The project references every program and library and lays out every shipped
plugin under `plugins\`, so a feature of the editor, the viewer, the CLI, the
preset site or a plugin takes its scenario here like any other.
Building never compiles; each check compiles for its own sink. The sound steps
evaluate the audio program at 1 kHz without the renderer's filters, so a sample is
exactly what the patch computed. C# tests cover the edges.

### Skips and optional outputs

A test that needs something the machine may lack says so and skips:

```csharp
Assert.SkipWhen(Encoder is null, "no ffmpeg on this machine");
Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows backend only opens on Windows.");
```

Never a custom attribute, and never a silent pass. The exception is the shot
tests (`PatchShotTests`, `SkinShotTests`), which write the website's pictures when
`SHOT_DIR` is set and return early when it is not, so an ordinary run neither
writes files nor fails.

### Speed

The median test takes under a millisecond. Anything over a second has to explain
itself, and an unexplained one is a defect, fixed in a commit of its own. After a
run, rank by duration:

```bash
./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -xml results.xml
```

The usual causes are waiting on a wall clock, waiting out a deadline to prove
nothing happened, leaving something running that later tests pay for, and
rebuilding per test what the class could build once.

- A spin-wait pumps the dispatcher and checks the clock, with a deadline:
  `while (!done() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }`.
  It asserts with a message when the deadline passes.
- A wait that proves a negative is capped hard, and a comment says why that cap is
  enough.
- A test that is honestly slow says why: "Twelve seconds because the faders are
  the slow part — the quickest is a sixth of a hertz."
- The background IL compiler has `Settled()` for tests. Await it; do not sleep.

### What a change needs

A behavior change lands with a test that fails before it and passes after. A bug
fix lands with the failing test that confirmed the bug, filed under the rule it
broke. `/bughunt` is that loop as a command.

---

## 13. Recipes

**Add a built-in module.** Add a `NodeDef` to the right `NodeCatalog.*.cs` partial
with a type id that will never change. Build it from existing ops if at all
possible: a module made only of existing ops needs nothing else, and survives to
the shader for free. Every-module and every-preset theories pick it up. Update
`PublicAPI.Unshipped.txt` if the surface moved, the site if it names modules, and
the changelog.

**Add an opcode.** Last resort; first try composing existing ops, then a stateful
cell ([0041](adr/0041-a-plugin-can-hold-state-without-a-new-opcode.md)). It
touches `OpCode.cs` (next number, never a reused one), `OpShape.cs`,
`CompiledPatch.Run`, `IlOps` and `IlEmitter`, `GlslEmitter` (or the named list of
ops that write nothing), and the `Emitter` if it needs a helper. `TotalityTests`,
`IlProgramTests` and `GlslEmitterTests` fail until all of them agree, and the GLSL
snapshots change. It moves the plugin contract's minor.

**Add a module from a plugin.** Copy `tests/Flyback.Plugins.Sample`. Type ids
start with the provider id. Add a `PluginProject` item to `Flyback.App.csproj` and
to `Flyback.Plugins.Tests.csproj`. Update `site/plugins.html` if the contract
moved.

**Add a CLI flag or command.** Declare it in `Program.cs`, run it in the command's
own file, test it in-process through the two writers. Update the README's CLI
section and the site in the same commit.

**Add a setting.** A plugin's own settings are declared as `SettingField`s
([0085](adr/0085-a-sound-backend-declares-its-own-settings.md)) and drawn by the
shell. The app's own go in the matching `*.json` settings class, loaded tolerant
of a missing or damaged file.

**Add a preset.** Use the `authoring-presets` skill, and `played-presets` if it
has MIDI voices or panel knobs. It must compile clean, fit the canvas, and stay
inside what the speakers carry; `ShippedPresetTests` checks the first two.

**Change how something looks.** Change the token in `Colors.cs` or `Text.cs`, then
`site/assets/site.css`, then retake the affected screenshots from the real app
(`site-screenshots` skill). Never draw a module in HTML, CSS or SVG
([0119](adr/0119-the-website-shows-a-module-by-photographing-one.md)).

**Look at a patch.** `flyback-viewer`, with `--mute` unless the sound is the
point. A still is `flyback-cli render -o shot.png --at <seconds>`. The editor is
only for questions about the editor.

---

## 14. Refactors that are already declined

A first scan of the code surfaces these, and each has been decided:

- **View models for `MainWindow`.** Declined by
  [0016](adr/0016-build-the-ui-in-c-sharp-without-xaml.md) and again by
  [0148](adr/0148-the-window-is-two-hubs-and-the-regions-around-them.md).
- **Unifying the interpreter, IL and GLSL opcode switches.** The GLSL is a
  transcription because its builtins disagree with the specification
  ([0035](adr/0035-a-glsl-backend-for-the-video-path.md)). The coverage tests are
  the guard.
- **Sharing the OpenAI and Gemini session loops.** Deliberate; see
  `GeminiSession.cs`.
- **SIMD in the interpreter.** [0006](adr/0006-scalar-interpreter-parallel-over-rows.md).
- **Incremental recompilation.** [0021](adr/0021-recompile-the-whole-patch-on-every-edit.md).
- **An imaging, audio or JSON library in the engine.**
  [0019](adr/0019-no-third-party-dependencies-in-the-engine.md).

The refactors worth proposing are where the code has drifted from a decision, or
where a pattern the repo established was never extended. ADR-0017 still describes
a node editor a fraction of its current size; a color or type-size literal outside
`Colors.cs` and `Text.cs` is the same kind of drift. When ranking candidates,
count code lines rather than total lines, since comments are a third to two thirds
of most files.
