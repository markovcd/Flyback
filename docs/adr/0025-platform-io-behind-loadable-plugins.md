# ADR-0025: Platform I/O behind plugins loaded at run time

**Status:** Accepted · 2026-08-12 · amended by
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md), which extends
plugins to modules once a patch records where its modules came from

## Context

[0024](0024-audio-device-in-the-shell.md) put the device behind `IAudioDevice`
and implemented it once, with NAudio, inside `Flyback.App`. That was the right
seam and the wrong side of the boundary: the shell took a `PackageReference` on
a Windows-only backend, so `NAudio.Wasapi.dll` shipped in the output of a program
that [0015](0015-avalonia-for-the-ui-shell.md) chose Avalonia in order to run on
Linux.

Adding the second implementation 0024 anticipated makes it worse rather than
better. Referencing ALSA, PortAudio and CoreAudio bindings together means every
build carries every platform's dependencies and their native assets. Splitting
them across `net10.0-windows` and `net10.0-linux` target frameworks multiplies
the build and puts `#if` in the shell. Both answers make a decision at compile
time that is only knowable at run time — which machine this is.

## Decision

Backends are separate assemblies, discovered on disk and loaded when the program
starts. Three pieces:

**`Flyback.Plugins`** — the contract, and the loader. No dependencies, same rule
as [0019](0019-no-third-party-dependencies-in-the-engine.md). A plugin is one
class:

```csharp
public interface IFlybackPlugin
{
    PluginInfo Info { get; }
    void Register(IPluginRegistry registry);
}
```

`IPluginRegistry` is what a plugin may contribute. Today that is one method,
`AddAudioOutput`. A new kind of extension is a new method on the registry, which
existing plugins only ever call and therefore never breaks them.

**`IAudioOutput` is split from `IAudioDevice`.** The first describes a backend
that might exist; the second is hardware that is open. So the host can ask what
is available, and choose between backends, without touching a device — and a
plugin for another operating system answers `IsSupported => false` rather than
throwing from a constructor.

**`Flyback.Plugins.Wasapi`** — 0024's device, unchanged in substance, with the
NAudio reference that used to be the shell's.

The layout is one folder per plugin, each with its own `AssemblyLoadContext`:

```
Flyback.exe
plugins/
  Wasapi/
    Flyback.Plugins.Wasapi.dll
    Flyback.Plugins.Wasapi.deps.json
    NAudio.dll …
```

`AssemblyDependencyResolver` reads that `.deps.json`, which is also what marks
the entry assembly among its own dependencies — no manifest file to keep in step
with the build. The contract assembly itself always resolves to the host's copy;
loading a second one would give its types a second identity, and the plugin
would silently stop being recognised as a plugin.

Selection is by `Priority` among supported backends, ties broken on `Id` so two
runs on one machine agree.

## Consequences

`Flyback.App` no longer references any backend. Its build output contains
`Flyback.dll`, `Flyback.Core.dll`, `Flyback.Plugins.dll` and Avalonia — nothing
platform-specific — which is what makes 0015's claim true again rather than
aspirational. **This lifts the narrowing 0024 applied to it.** Sound on Linux is
now a project someone can add without touching, or rebuilding, the shell.

The failure modes are all quiet. No plugin folder, an empty one, a plugin built
against another runtime, one that throws from its constructor — each is a line in
`PluginCatalog.Problems` and a disabled Audio button, never a program that will
not start. `SilentAudioDevice` is what the engine gets when nothing can play, so
there is no second code path and no null to check. It is deliberately not a
clock: it would freeze the preview, which follows the audio cursor, so the shell
disables sound outright rather than pretending to play.

Shipping a plugin in the box costs one item in the consuming project:

```xml
<PluginProject Include="..\Flyback.Plugins.Wasapi\Flyback.Plugins.Wasapi.csproj"
               FolderName="Wasapi" />
```

`Directory.Build.targets` builds it *into* `plugins\Wasapi\` rather than copying
it there, because the SDK already knows which of a project's dependencies belong
beside it and a hand-written copy step would have to guess. The one sharp edge:
the redirected `OutDir` is a global property and flows down through project
references, so the reference to the contract carries
`GlobalPropertiesToRemove="OutDir"` — without it the contract is rebuilt into the
plugin folder, which is the one thing that must not happen.

Because the plugin is a real assembly in a real folder, the tests load the
shipped one rather than a stub, and can assert the thing that would otherwise
fail silently: that a type crossing the boundary is the same type on both sides.

There is no reload. An assembly the audio thread is calling into cannot be
unloaded safely, and a collectible context that only *usually* unloads is worse
than restarting the program. Plugins also run in-process with full trust — this
is an extension point, not a sandbox, and a hostile plugin has the same reach as
the shell.

**This does not extend to modules.** *(Reversed by
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md): the objection
below is to a dependency that is silent, and a patch that records what it needs
does not have one.)* [0008](0008-modules-as-data-in-one-catalogue.md)
keeps every module in one catalogue and
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) keys saved patches by
its type ids; a plugin that added modules would make whether a `.fbk` file opens
depend on what is installed. Plugins are for the platform edge — the places where
the answer differs per machine — not for the engine, where it must not.
