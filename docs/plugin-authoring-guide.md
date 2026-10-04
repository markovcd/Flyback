# Authoring Flyback plugins

Everything that differs per machine, and everything the engine deliberately does not know about, arrives from a folder on disk. This is how to put something in it.

**Written for plugin contract 1.0.0** (`Flyback.Core` and `Flyback.Plugins` assembly version `1.0.0`), on .NET 10 (`net10.0`). The contract has a version of its own, apart from the application's; [Version compatibility](#version-compatibility) says how the two relate and what loads where.

This guide assumes you do not have Flyback's source. Everything you compile against is in an installed Flyback: the folder holding the executable contains `Flyback.Plugins.dll` and `Flyback.Core.dll`, and that is the whole contract. Each DLL ships with its `.xml` documentation file beside it (`Flyback.Core.xml`, `Flyback.Plugins.xml`), and every public member is documented, so your IDE shows descriptions on hover as long as you reference the DLLs from that folder.

## Contents

1. [What a plugin is](#what-a-plugin-is)
2. [Version compatibility](#version-compatibility)
3. [The project file](#the-project-file)
4. [The entry point](#the-entry-point)
5. [What you can contribute](#what-you-can-contribute)
6. [Authoring a module](#authoring-a-module)
7. [Modules that remember](#modules-that-remember)
8. [Shipping presets](#shipping-presets)
9. [Sound and MIDI backends](#sound-and-midi-backends)
10. [Building and installing](#building-and-installing)
11. [Packaging and sharing](#packaging-and-sharing)
12. [How things fail](#how-things-fail)
13. [Testing](#testing)
14. [Rules of thumb](#rules-of-thumb)
15. [Discovering the surface](#discovering-the-surface)

## What a plugin is

A plugin is one .NET assembly in its own folder under `plugins/`, beside the executable. Nothing references it at build time. The host enumerates the folders at startup, loads each into its own `AssemblyLoadContext`, and asks whatever it finds inside what it would like to contribute.

Two assemblies are **host-owned**, and together they are the plugin contract:

| Assembly | What it is |
|---|---|
| `Flyback.Plugins` | The extension surface: `IFlybackPlugin`, `IPluginRegistry`, the sound, MIDI, secret-store and assistant interfaces. |
| `Flyback.Core` | What a patch and a module are made of: the graph, sockets, `NodeDef` and its extras, the `Emitter` a module lowers itself through, and the built-in modules a preset can name. |

A plugin compiles against both and ships neither. That is not a convention: it is what stops one type having two identities and every cast across the boundary failing. A stray copy in your folder is ignored and the host's is used.

What runs a patch is a third assembly, `Flyback.Engine` (compiler, text language, renderers, file formats). **A plugin never references it.** It changes between releases without a plugin built against an older contract noticing.

All three sit beside the executable, so the two you need are there to compile against. Each plugin gets its own load context, so two plugins may depend on different versions of one package.

## Version compatibility

The contract carries its own version. It is the assembly version of `Flyback.Core.dll` and `Flyback.Plugins.dll` and of nothing else; the application's release number is unrelated. This guide is written for **contract 1.0.0**.

To see which contract an installed Flyback offers, read the assembly version of either DLL in its folder:

```powershell
(Get-Item "C:\Path\To\Flyback\Flyback.Core.dll").VersionInfo.FileVersion
```

```csharp
var offered = System.Reflection.AssemblyName.GetAssemblyName(@"C:\Path\To\Flyback\Flyback.Core.dll").Version;
```

The contract version is not the same as the file or product version that Explorer shows; the `AssemblyName` read above is the one the host compares.

| Part | Moves when | Cost |
|---|---|---|
| major | something a plugin could have named was removed or changed | every plugin built against the previous major is refused until rebuilt |
| minor | something was added | a plugin built against the new minor is refused by a host that only offers the old one |
| patch | never | a fix that changes no signature changes nothing a plugin compiled against |

The compiler stamps the version of each contract assembly into your plugin, so you declare nothing. Before any of your code runs, the host reads those references back and applies one rule:

| Plugin built against | Host offers | Result |
|---|---|---|
| same major, same or earlier minor | any release offering that contract | loads |
| earlier major | later major | ignored: *needs rebuilding* |
| later major, or same major and later minor | an older contract | ignored: *needs a newer Flyback* |

A refusal is a line in the plugins window (and in `flyback-cli plugin list`) that says which side to replace. It is never a crash on whichever thread first reaches for a member that is gone.

What this means in practice:

- **Build against the oldest Flyback you mean to support.** The contract you compile against sets the oldest host your plugin loads in, not the newest. The same plugin keeps loading in later releases for as long as its major is still offered.
- **Releases do not break you by themselves.** A release that changed nothing a plugin can see leaves the contract version alone, so the application's version moving does not matter.
- **Plugins built before 1.0.0 are refused.** Earlier builds carry a release's `0.x` assembly version; before the contract had a version there was nothing for them to have been built against. Rebuild them against 1.0.0.
- **Only the two DLLs are the surface.** Whatever is public in them is the promise; anything you reach by reflection, or by copying another assembly out of the install folder, is not.

What may change without a new major (so what you can rely on):

- `IPluginRegistry` and `Emitter`, which you call, gain members freely.
- `IAudioOutput`, `IMidiInput`, `ISecretStore` and `IPatchAssistant`, which you implement, gain a member only with a default body or as a second interface the host looks for.
- A record's positional parameters are closed; what is new arrives as an `init` property (as on `NodeDef` and `EmitContext`).
- An enum is numbered by hand and only added to at the end. `OpCode` numbers never move. A constant's value never changes.
- `NodeExtra`, which you derive from, gains only virtual members with a body.
- Nothing is removed except at a major, and is marked `[Obsolete]` for the whole of the major before.

What the contract does **not** promise: shape is checked, behavior is not. A built-in module that gains a port in the middle, or lowers to something different, is judged by a person, not by the analyzer. Build modules on the ports you need and test them.

Saved patches name your modules by type id, forever: an id is never renamed, renumbered or reused once shipped, and a socket is saved by position, so new sockets go at the end.

## The project file

Four things matter, and each fixes a specific failure.

Create a class library and point it at the installed copy:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>

    <!-- The folder Flyback is installed in: the one holding the executable. -->
    <Flyback>C:\Path\To\Flyback</Flyback>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Flyback.Plugins" HintPath="$(Flyback)\Flyback.Plugins.dll" Private="false" />
    <Reference Include="Flyback.Core" HintPath="$(Flyback)\Flyback.Core.dll" Private="false" />
  </ItemGroup>

</Project>
```

- `EnableDynamicLoading` produces the `.deps.json` the resolver reads. Without it nothing but the entry assembly resolves and your first dependency fails at load.
- `Private="false"` compiles against the host's assemblies without copying them next to yours.
- **Name both references.** Referencing only one leaves the other to be pulled in some other way and copied beside your assembly, which gives its types a second identity.
- Reference packages of your own freely.
- **Leave `Flyback.Engine.dll` alone**, though it sits in the same folder. A plugin that references it is bound to the one release it was copied from, and is outside the contract.
- The release you compile against sets the oldest Flyback your plugin loads in. Install the oldest one you mean to support and point `<Flyback>` at it.

## The entry point

One public, non-abstract class with a parameterless constructor implementing `IFlybackPlugin`. The host finds it by reflection, constructs it, and calls `Register` once.

```csharp
using Flyback.Core.Graph;
using Flyback.Plugins;

[assembly: FlybackModule(RingsModule.TypeId, "Rings")]   // one per module Register adds

namespace Flyback.Plugins.Yours;

public sealed class YoursPlugin : IFlybackPlugin
{
    private static readonly ModuleProvider Provider = new("yours", "Your modules");

    public PluginInfo Info { get; } = new(
        "yours",                        // stable, machine-readable
        "Your modules",                 // what a person sees
        "One line about what this is.");

    public void Register(IPluginRegistry registry) =>
        registry.AddModules(Provider, [RingsModule.Definition]);
}
```

**The one hard rule about `Register`:** it runs on the UI thread during startup, for every installed plugin, before the window is usable. It must be cheap, and it must not open a device, read a credential or reach the network. Whether something can actually run belongs on the thing being registered (`IsSupported`).

Declare every module `Register` adds with a `FlybackModule` attribute on the assembly, giving the type id and name its `NodeDef` has. It is how the install dialog and the shared plugins site list your modules without running your code. After `Register`, Flyback checks it: a plugin that registers a module it did not declare, or under another name, is refused whole. A module declared and not registered is fine, for one you leave out on some systems. `flyback-cli pack-plugin` runs the same check.

An assembly may hold more than one plugin class, and each is instantiated. Two plugins claiming the same `Info.Id` is refused: the first wins.

## What you can contribute

`IPluginRegistry` has six methods. New kinds of extension arrive as new methods, so a plugin compiled against an older minor keeps working.

| Method | Contributes | Must not, when registering |
|---|---|---|
| `AddModules` | Modules in the palette, keyed by a provider | |
| `AddPresets` | Patches in the preset gallery | |
| `AddAudioOutput` | A sound backend | Open a device |
| `AddSecretStore` | Somewhere the OS holds a secret; honored only from a plugin Flyback ships or one allowed with `--secrets` | Read or write one |
| `AddPatchAssistant` | Something that can author a patch over the `IAssistantTransport` it is handed; the host puts the key on requests to the origin it was entered for, and the assistant never holds it | Open a connection, or need a credential |
| `AddMidiInput` | A MIDI backend | Open a device, or list the ones plugged in |

One plugin may call several. A platform plugin typically adds a sound backend and a MIDI input and nothing else; a module plugin adds modules and presets. Every kind is keyed by an id, and an id registered twice is refused to both.

## Authoring a module

A module is one `NodeDef`: its sockets, and a function that lowers it to ops. It is the same data the built-in modules are declared with; there is no second-class plugin module.

```csharp
using Flyback.Core.Compile;
using Flyback.Core.Graph;

public static class RingsModule
{
    public const string TypeId = "yours.rings";

    public static NodeDef Definition { get; } = new(
        TypeId, "Rings", "Pattern",
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across) { Standard = true },
            new PortSpec("y", NormalledTo: NodeCatalog.Down) { Standard = true },
            new PortSpec("freq", PortKind.Scalar, 4f, 0f, 32f) { Help = "Rings from the middle to the edge." },
        ],
        [new PortSpec("out") { Help = "The rings, -1 to 1." }],
        (em, i) =>
        {
            var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
            return [em.Unary(OpCode.Sin, em.Mul(em.Mul(radius, i[2]), MathF.Tau))];
        },
        "Concentric sine rings.");
}
```

### The type id prefix rule

Every type id must begin with your provider's id and a dot. That one rule makes shadowing a built-in impossible, makes a collision between two plugins impossible, and lets the provider of a module be read off a saved patch without having the plugin. An id that breaks it is dropped individually: one bad module costs that module, not the plugin. The provider id `flyback` is reserved.

### Sockets

`PortSpec` carries more than a name. Most of it is the editor's business: a stored number means the same whatever these say.

| Field | What it does |
|---|---|
| `Help` | What this one socket is for, in a sentence or two that stand alone. The inspector shows it as the tip on the socket's row and the assistant reads it. Give every socket one. |
| `Standard` | Set in place of `Help` where the socket means what its name means everywhere (`x`, `y`, `freq`, `phase`, `amp`, `bias`, `mix`, ...). A socket that asks for a name with no standard is a mistake: the module is refused and the refusal names the socket. |
| `Kind` | `Scalar`, `Color` or `Any`. `Any` passes through whatever arrives, so one module works on both a tone and a picture. |
| `Default`, `Min`, `Max` | The knob on the node and its range. A `Color` input has no knob. On an output, `Min` and `Max` say what it puts out (`-1f, 1f` for a wave, `0f, 1f` for an envelope), which an Auto remap reads. |
| `Lenient` | A value past either end of the range still means something (a phase wraps, a gate reads a threshold). |
| `Knee` | The knob sweeps evenly below it and in decades above it. Give a frequency a knee at the bottom of its range. |
| `NormalledFrom` | Index of an earlier input this one falls back to when nothing is patched in. |
| `NormalledTo` | A module driving this input while nothing is patched into it, as a `PortNormal(typeId, port)`. The module is hidden and shared; the socket has no knob. `NodeCatalog.Clock`, `Across` and `Down` are the engine's. |
| `Display` | `Number`, `Note`, `Duration`, `Integer` or `Chord`. |
| `Domain` | The axis the module is read *across* rather than a value it uses. |
| `Swept` | The module supplies the domain this input is read *under*. Resolve it with `EmitContext.Resolve`. |
| `PatchOnly` | `Default` is a filler nobody should dial. The editor draws no knob. |

### Which sink a module is for

Every module is compiled for both sinks (picture and sound), because a patch is one graph. `Sinks` on the `NodeDef` says where it means what it says: `ModuleSinks.Both` (the default), `Audio`, or `Video`. It changes nothing that is emitted; the assistant's handbook reads it, so set it whenever it is not `Both`.

### How a module is drawn

By default a module is drawn as its category. `Skin`, an init property on the `NodeDef`, gives a module a background of its own. **Only the background changes**: shape, header, sockets and description stay.

```csharp
// A palette: drawn exactly the way every built-in module is.
Skin = new ModuleSkin.Palette(new Swatch(0x2E, 0x8B, 0x57))
{
    Floor = new Swatch(0x10, 0x20, 0x30),
    Glyph = "M4,12 A8,8 0 1,1 20,12 A8,8 0 1,1 4,12",
}

// A grain: the same palette with a texture cut across it.
Skin = new ModuleSkin.Grain(new Swatch(0x4A, 0x7E, 0xC8), GrainCut.Beaded)

// A picture: SVG, PNG or GIF, scaled to cover the body and clipped to it.
Skin = new ModuleSkin.Artwork(bytes) { ContrastText = true }

// Two pictures: the block's, and one of the panel's own shape.
Skin = new ModuleSkin.Artwork(block) { Panel = tall }
```

`Palette.Accent` is the one color everything is worked out from; `Floor` is what the wash falls to; `Glyph` is SVG path data on a 24-unit box. `Grain.Cut` is `Hatched`, `Milled` or `Beaded`. `Artwork.Bytes` is the picture itself, usually read from an embedded resource; bytes that are not a picture fall back to the category. `ContrastText` derives text color from the background behind it instead of drawing white; use it for pale or busy backgrounds.

The person using Flyback has the last word: *Settings > Canvas* can turn off plugin skins entirely and hold animations at their first frame.

### Carrying something that is not a knob

Some decisions are about the piece rather than signals in it (a sequencer's notes, a quantiser's scale). Declare a `NodeExtra`: a key and the values it holds. The shell draws the rows from what you declared, so a plugin never references Avalonia.

```csharp
public sealed record GlideExtra : NodeExtra
{
    public override string Key => "glide";

    public override IReadOnlyList<ExtraField> Fields =>
    [
        new ExtraField.Number("time", "time", new PortSpec("time", PortKind.Scalar, 0.1f, 0f, 1f))
        {
            Help = "How long a note takes to slide to the next, in seconds.",
        },
        new ExtraField.Toggle("legato", "legato") { Help = "Slides only between notes that overlap." },
    ];
}

// on the NodeDef
Extras = [new GlideExtra()],

// in the emit function
var state = node.Extra<ExtraState>("glide");
var time = state?.Number("time") ?? 0.1f;
```

Four field shapes exist: `Number` (takes a whole `PortSpec`, so it gets the same slider and formatting as a knob), `Toggle`, `Choice` (a list of `ChoiceOption(id, name)`, read with `state.Chosen(key)`), and `Text` (read with `state.Text(key)`). Give every field a `Help`. Your values live under your key in the patch file and round-trip whether or not the plugin is loaded; every read is held to the range you declared.

### The emit function

It runs **once, at compile time**, and writes straight-line ops into a flat register machine:

- It never sees a knob's value, so nothing about the shape of what you emit can depend on one.
- There are no branches. A choice is arithmetic (`Step`, `Mix`, `Clamp`), not an `if`.
- It runs whole or not at all: reaching for one output compiles everything upstream of all your inputs.
- Loops that unroll are fine.

The `Emitter` gives you `Unary`, `Binary` and `Ternary` over `OpCode`, plus `Constant`, `Load` (the pixel's x, y and t), and the shorthands `Add`, `Sub` and `Mul`. Widths broadcast like a shading language: a scalar meeting a color applies to all three channels.

## Modules that remember

A picture is rendered by evaluating the program once per pixel, in parallel and in no particular order, so most modules are pure functions of `(x, y, t)`. A module with a memory works for the speakers and has to say what it means for the screen. There are four tiers:

| Tier | Mechanism | Notes |
|---|---|---|
| Pure | none | Identical at both sinks, free on the video path. Reach for this unless you cannot. |
| One evaluation | Emitter cells | A value carried from the last evaluation to this one: filters, slew limiters, sample-and-hold. Any plugin. |
| A plane | Emitter planes | A cell the screen keeps too: one value per pixel from the previous frame. Any plugin. |
| A buffer | `Delay`, `Allpass` opcodes | Thousands of past samples. Needs an opcode, so use the two that exist. |

### Taking a cell

```csharp
var cell = em.AllocateUnitSlot();
var previous = em.UnitRead(cell);        // what it held last time
var smoothed = em.Ternary(OpCode.Mix, previous, input, amount);
em.UnitWrite(cell, smoothed);            // what it holds next time
```

Read before you write; the gap is the one evaluation of latency that makes it a memory. A value written is clamped to the rails on the way in. A cell holding a time rather than a signal is written with `ClockWrite`, which is not clamped.

### Taking a plane

`AllocatePlaneSlot`, `PlaneRead` and `PlaneWrite` are the same three calls, but on the video path the value is kept per pixel from one frame to the next. On the audio path a plane behaves like a cell. Every plane costs a value per pixel; do not reach for one to make a filter work on a picture.

### Lowering part of a module once

A module read through a `Swept` input is lowered once per place it is read at. Whatever does not depend on the place goes behind `em.Once`:

```csharp
var ring = em.Once("ring", [trigger, velocity], () => Ring(em, trigger, velocity));
```

Name every slot the lambda reads that it did not make; x, y and t are noticed on their own.

### The two questions every stateful module has

- `em.Interval()`: how far the clock moved since the previous evaluation. It is the sample rate said the other way round, and what turns a cutoff in hertz into a coefficient.
- `em.HasMemory()`: one where the program has state behind it, zero where it has none. An emit function runs before anything knows which sink will run it, so this flag is the only way to ask.

```csharp
// A picture is one evaluation with nothing before it, so what a filter
// sees there is a signal that never moves.
var live = em.HasMemory();

return
[
    em.Ternary(OpCode.Mix, dry, lowpass, live),   // a wire, with no state
    em.Mul(bandpass, live),                       // silent
    em.Mul(highpass, live),                       // silent
];
```

With no state, `UnitRead` gives zero, `UnitWrite` goes nowhere, a `Delay` hands its input straight through and `Phase` becomes the multiply it replaced. The fallbacks are total and will not crash, but whether they mean anything is yours to decide. If your module is for the picture, keep it to arithmetic: the preview runs it as a shader.

### Buffers

```csharp
var echoed = em.DelayLine(OpCode.Delay, input, feedback, time, maximum);
```

`time` is a signal and may be swept (reads interpolate); `maximum` is fixed at compile time because it sizes a buffer. `OpCode.Allpass` has the same shape and smears without coloring. Two seconds at the oversampled audio rate is about 1.5 MB per line; ask for what you need.

## Shipping presets

A preset is a name and a function. The function is handed the catalog when the preset is *picked*, not when it is registered, so it can use the modules the same plugin just added.

```csharp
registry.AddPresets(
[
    new PatchPreset("Your preset", Build, "What the preset is for.", PresetKind.Idea),
]);

static Patch Build(ModuleCatalog modules)
{
    var b = new PatchBuilder(modules);

    var coord = b.Add("coord");
    var rings = b.Add("yours.rings", (2, 2.5f));   // (port, knob)
    var output = b.Add(NodeCatalog.OutputTypeId);

    b.Wire(coord, 0, rings, 0)
     .Wire(coord, 1, rings, 1)
     .Wire(rings, 0, output, NodeCatalog.OutputColorPort);

    return b.Build();
}
```

- The third argument is a line that becomes the patch's description (the gallery shows it under the name), unless the patch sets one with `patch.Describe(...)`.
- `PresetKind` picks the gallery heading: `Idea` (the default), `Interplay` where sound and picture are the same thought, `Showcase` for a whole piece.
- Say nothing about coordinates. `b.Build()` lays the patch out; taking `b.Patch` instead hands over a pile at the origin.
- Every one- and two-input Maths module in your preset is folded into an Expression as it is built. The result sounds and looks as you wired it.

A preset that plays a sound file or shows a picture carries it inside your plugin:

```xml
<!-- in the .csproj -->
<EmbeddedResource Include="Speaking\*.wav" LogicalName="Speaking/%(Filename)%(Extension)" />
```

```csharp
new PatchPreset("Speaking", Build)
{
    Files = PresetFiles.Embedded(typeof(YoursPlugin).Assembly, "Speaking"),
};

// and in Build, a Sample named by the file's name in the folder
SampleExtra.Set(b.Add(NodeCatalog.SampleTypeId), "hello.wav");
```

A preset that reaches for another plugin's modules is allowed; it is built when picked, so check first with `modules.HasProvider(id)` and throw something that names the plugin.

## Sound and MIDI backends

The application has no platform-specific code in it; every sound device is a plugin. A backend is two classes: one that offers itself, one that opens the device.

```csharp
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;

public sealed class JackOutput : IAudioOutput
{
    public string Id => "jack";
    public string Name => "JACK";
    public int Priority => 150;      // the shipped ALSA backend is 100, and loses
    public bool IsSupported => OperatingSystem.IsLinux() && JackIsRunning();

    // Optional: what the Sound settings tab shows for this backend.
    public IReadOnlyList<SettingField> Form(SettingValues values) =>
    [
        new SettingField.Pick("port", "Port", JackPorts(), "system"),
    ];

    public IAudioDevice Create(AudioFormat format, SettingValues settings) =>
        new JackDevice(format, settings.Text("port", "system"));
}
```

`IsSupported` must answer without opening anything and without throwing, which is what lets a plugin for another operating system stay loadable everywhere. Higher `Priority` wins; ties break on `Id`.

`Form` declares settings in the vocabulary an assistant uses (`Text`, `Pick`, `Switch`). The settings window draws them, keeps the answers in `settings.json` under your backend's id, and hands them to `Create` at launch and on every Save that changes them. Open nothing until `Start`.

`IAudioDevice.Start` is handed a callback that fills an interleaved stereo buffer on the audio thread. It **must not block, allocate or throw**. If you cannot honor the requested format, open the nearest and report the truth through `SampleRate`. Report the callback-to-speaker delay through `Latency`.

### MIDI inputs

```csharp
using Flyback.Plugins.Midi;

public sealed class JackMidiInput : IMidiInput
{
    public string Id => "jack-midi";
    public string Name => "JACK MIDI";
    public int Priority => 150;
    public bool IsSupported => OperatingSystem.IsLinux() && JackIsRunning();

    // Asked afresh whenever somebody opens a picker.
    public IReadOnlyList<MidiPortInfo> Ports => ListPorts();

    public IMidiPort Open(string port, MidiCallback deliver) =>
        new JackMidiPort(port, deliver);
}
```

`Ports` must not open anything or throw: a vanished device is a shorter list. `Open` may throw, for a device another program has taken. The callback runs on whatever thread your backend hears on, so it must not block. Hand it a `MidiMessage`; `MidiMessages.Of(status, data1, data2)` turns three raw bytes into one.

## Building and installing

Drop your build output into a folder under `plugins/` beside the executable (assembly, dependencies and `.deps.json` together) and allow it. A folder copied in by hand is a stranger's code until somebody says yes, so a Release build loads it only once allowed, and only while its files are as they were then. A Debug build loads every folder.

```bash
flyback-cli plugin allow plugins/Flyback.Plugins.Yours
flyback-cli plugin allow plugins/Flyback.Plugins.Yours --secrets
flyback-cli plugin list --json
flyback-cli plugin deny plugins/Flyback.Plugins.Yours
```

`--secrets` lets it register a secret store. `plugin list` says which folders load and why the others do not. The yes is kept in the data folder, never in `plugins/`.

Two things catch people out:

- **Plugins are read once, at startup.** Installing one means restarting the application.
- **The command line and the viewer have no plugins of their own.** All three programs share one folder beside them; run from a publish rather than from the shell's build output and they will not find your modules.

## Packaging and sharing

To hand a plugin to somebody else, pack it as a signed `.fbkp`:

```bash
flyback-cli plugin-key -o ripple.key
flyback-cli pack-plugin Flyback.Plugins.Ripple.csproj -o ripple.fbkp --key ripple.key
flyback-cli pack-plugin bin/Release/net10.0 -o ripple.fbkp --key ripple.key
flyback-cli plugin describe ripple.fbkp --json
```

Make the key once and keep it out of source control. Every package of the plugin is signed with it: Flyback takes a package as an update only when the same key signed both. A lost key means your users remove the plugin before the next build installs. A package nobody signed is shown but not installed, except by a Debug build.

Given a project, `pack-plugin` runs `dotnet publish` once per runtime in `<RuntimeIdentifiers>` (or once portably) and needs the .NET SDK. Given a folder the SDK already built into, it needs nothing. It holds each build to the project file above: no `runtimeconfig.json` or a bundled copy of `Flyback.Core` or `Flyback.Plugins` is refused. `plugin describe` prints what the install dialog will show for any package without running it.

A package is a zip with a folder per system (`win`, `osx`, `linux`, or `any`) and nothing else. There is no manifest: the dialog reads the plugin assembly's own metadata.

```xml
<PropertyGroup>
  <Product>Ripple</Product>
  <Version>1.2.0</Version>
  <Authors>Ada</Authors>
  <Description>Rings that spread from wherever a note lands.</Description>
</PropertyGroup>

<ItemGroup>
  <AssemblyMetadata Include="Tags" Value="texture; feedback, generative" />
  <EmbeddedResource Include="preview.webp" LogicalName="preview.webp" />
</ItemGroup>
```

- Name, version, author, description: `<Product>`, `<Version>`, `<Authors>`, `<Description>`.
- Tags: an `AssemblyMetadata` item named `Tags`, split at commas and semicolons.
- Preview: a PNG or WebP up to 1 MB embedded as `preview.png` or `preview.webp`.
- What it adds and what it reaches (network, files, native code, ...): read from the code without running it.
- Its modules: from the `FlybackModule` declarations.

Each build holds exactly one plugin assembly at its top; its name is the folder it installs into. A package signed with the same key replaces the installed one whole at the next start, offered as an update, reinstall or downgrade by comparing `<Version>`. This `<Version>` is the plugin's own and has nothing to do with the contract version.

A package is refused if a file in it could land outside the plugin's folder, if two files differ only in case, if it exceeds 128 MB (512 MB unpacked), or if its preview is wrong. Install is off when there is no build for this system, when the plugin or any assembly it carries was compiled against a contract this Flyback does not offer, or when it adds modules and declares none. The dialog shows the contract versions the plugin was built against, the signing key's fingerprint and the package's SHA-256.

To offer a package to everyone, submit it at the preset site's plugin page. It is read as the editor reads it, goes up unpublished, and is listed after review (up to 64 MB). Once published, the assembly name is bound to your key.

## How things fail

A broken, missing or hostile plugin is a note in the plugins window, never a program that fails to start. Failures are values, scoped as narrowly as possible: one bad module costs that module.

| Situation | Result |
|---|---|
| A type id missing the provider prefix | That module is ignored |
| The same type id twice | The later one is ignored |
| Provider id is `flyback` | Refused; reserved for the engine |
| Provider id already loaded, or blank | Refused |
| A provider claiming ids that already exist | Refused |
| Two plugins with one `Info.Id` | The second is ignored |
| A preset name already offered | The later preset is ignored |
| A sound backend, MIDI input, assistant or secret store id registered twice | Refused to both, each named |
| A folder nobody allowed, in a Release build | Not loaded, listed with the command that allows it |
| An allowed or shipped folder with a file changed since | Not loaded, naming the file |
| A secret store from a plugin not allowed to keep keys | Refused; the plugin loads without it |
| A stray host-owned dll in your folder | Ignored; the host's copy is used |
| A folder under `plugins/` starting with a dot | Never scanned |
| A package's plugin sharing an id with one shipped or copied in | The package's is ignored |
| Built against an earlier major of the contract | Ignored: needs rebuilding |
| Built against a later contract than this release offers | Ignored: needs a newer Flyback |
| A module registered that was not declared, or under another name | Ignored whole, the module named |
| A throw from `Register` | Ignored whole, reported |
| A throw from a preset | Reported; everything else carries on |

A provider appears only if at least one of its modules was accepted.

## Testing

Test against your real build, not a stub: a separate assembly, its own dependencies, and a deps file the resolver has to read. The quickest whole-plugin check is `flyback-cli plugin allow` on your output folder (or a Debug Flyback, which loads every folder) and `flyback-cli plugin list --json`, which says whether it loaded and why not.

For unit tests, a test project of your own may reference `Flyback.Engine.dll` from the install folder in addition to the two contract DLLs. The plugin itself never does; finding out what a module sounds like means compiling and running it, and that is the engine's half. Engine types are not part of the contract, so pin the test project to the Flyback you test against.

```xml
<Reference Include="Flyback.Plugins" HintPath="$(Flyback)\Flyback.Plugins.dll" />
<Reference Include="Flyback.Core" HintPath="$(Flyback)\Flyback.Core.dll" />
<Reference Include="Flyback.Engine" HintPath="$(Flyback)\Flyback.Engine.dll" />
```

To drive a module sample by sample, feed the signal in through the Coordinates module's `x` and the clock through `t`:

```csharp
var program = patch.CompileForAudio(catalog).Program;
var delays = new DelayState(program, rate);

var registers = program.AllocateRegisters();

for (var i = 0; i < signal.Length; i++)
{
    program.Evaluate(signal[i], 0f, i / (double)rate, registers, default, delays);
    output[i] = (float)registers[program.OutputBase];
}
```

Compile for video as well and pin what the module does with no state (passing `null` for the delays is what the renderer does for a program that keeps no planes). A test that says "with no state this is a wire" is worth more than one that says the filter filters.

`flyback-cli check` compiles both sinks of a patch and carries the answer in its exit code, which makes it the one to put in CI.

## Rules of thumb

- **Be a wire at zero.** An effect is exactly its input at a mix of zero. It costs nothing and makes a module comprehensible by turning one knob.
- **Normalize as you go.** Turning a knob up should make the sound dirtier or wider, never louder.
- **Clamp what persists.** Degenerate arithmetic is forgotten immediately; a bad value in a cell or a delay line is not.
- **Type your maths ports `Any`.** A module that shapes a number shapes a color for free.
- **Decide what you mean on the screen.** Not what the arithmetic leaves; what you would have chosen. Write it in the description.
- **Say it in the description.** It is the only documentation a person gets while patching, and the assistant reads it too.

## Discovering the surface

Without the source, `flyback-cli modules` lists every module with its sockets and help, and `flyback-cli plugin describe` shows what a package or build will present. The built-in modules your presets name are described there too. Any decompiler or IDE assembly browser on `Flyback.Core.dll` and `Flyback.Plugins.dll` shows the rest of the public surface.
