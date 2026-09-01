# ADR-0063: One plugin per platform, carrying its sound and its MIDI

**Status:** Accepted · 2026-09-01 *(user-directed)*

## Context

[0025](0025-platform-io-behind-loadable-plugins.md) put platform I/O behind
plugins loaded from disk, [0028](0028-publish-one-platform-at-a-time.md) let each
name the one platform it is for, and
[0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md) added a
MIDI backend for each of the three. Every one of those steps added a project the
way the one before it had, which left six projects for three platforms' worth of
input and output: `Wasapi` and `WinMidi`, `CoreAudio` and `CoreMidi`, `Alsa` and
`AlsaMidi`.

The six are not six things. They are three pairs, and no pair has ever been
separable:

- Both halves of a pair are supported on exactly one operating system, and it is
  the same operating system for both. `IsSupported` on the sound backend and
  `IsSupported` on the MIDI backend asked `OperatingSystem.IsWindows()` and got
  the same answer, always.
- Both halves of a pair carry the same `Platform` attribute in the manifest, so
  they are shipped together and left out together. A build that carried one
  without the other was never a build anyone could ask for.
- On Linux they are not merely paired but identical: `libasound.so.2` is what
  plays the sound *and* what routes the notes. Two projects meant the SONAME
  written out twice, and "is there a sound library on this machine at all" asked
  twice, through two byte-identical copies of the same `NativeLibrary.TryLoad`
  probe, one of which could drift from the other.

The cost was paid per pair and in several places at once. Six plugin folders,
six `.deps.json` files, and six `AssemblyLoadContext`s at startup for three
platforms. Twelve manifest lines across the shell and the test host, where six
would do. And six copies of the boilerplate every plugin project carries — the
two suppressed `ProjectReference`s, the `EnableDynamicLoading`, the comment
explaining why the contract must not be shipped — maintained in lockstep because
none of it differs.

None of this is a fault in the plugin boundary. It is the boundary being used at
the wrong grain: the unit that plugs in was taken to be the backend, when the
thing that ships, loads and is supported as one is the platform.

## Decision

**One plugin project per platform, named for the platform**:
`Flyback.Plugins.WinIO`, `Flyback.Plugins.MacIO`, `Flyback.Plugins.LinuxIO`,
built into `plugins/WinIO`, `plugins/MacIO` and `plugins/LinuxIO`. Each has a
single `IFlybackPlugin` that registers two things:

```csharp
public void Register(IPluginRegistry registry)
{
    registry.AddAudioOutput(new WasapiAudioOutput());
    registry.AddMidiInput(new WinMidiInput());
}
```

The registry has always taken more than one kind of contribution from one
plugin — a module plugin registers modules and presets together — so nothing in
the host changes to allow this.

**A plugin is named for the platform; a backend keeps the name of the interface
it speaks.** The plugin ids are `win.io`, `mac.io` and `linux.io`. The backend
ids are `wasapi`, `coreaudio`, `alsa`, `winmm`, `coremidi` and `alsaseq`,
untouched. A backend id is what a log line names and what a picker resolves
against; which assembly the class happens to live in is not a reason for the
thing itself to be called something else.

**On Linux the shared library is named once.** `LibAsound.Library` holds the
SONAME and `LibAsound.IsInstalled` is the one probe; `LibAsoundSeq` imports
against the former and the sequencer backend asks the latter. The two binding
files stay separate, because a PCM device and the sequencer are genuinely
different interfaces with different rules — but they no longer disagree about
what they are bound to.

**The interop stays one file per framework.** `WinMm`, `AudioToolbox`,
`CoreFoundation`, `MidiServices`, `LibAsound` and `LibAsoundSeq` are unchanged
apart from their namespace. Sharing an assembly is not a reason to share a file:
each is still the slice of one system library that one backend needs, and that is
the boundary worth keeping.

## Consequences

Three plugin folders where there were six, three load contexts at startup where
there were six, and six manifest lines across the shell and the test host where
there were twelve. The boilerplate that differed in no respect is now written
three times rather than six.

`AllowUnsafeBlocks` is now set for a whole platform assembly where only one half
of it needs the property — the WASAPI device is entirely marshalled by NAudio and
has no pointer in it. This is a real if small loss of precision: the compiler
will no longer object if unsafe code appears in the sound half of a platform
plugin. It is the price of a property being per-assembly, and the alternative —
keeping two assemblies so that one of them can be checked more strictly — is not
worth the other five costs above.

NAudio is still a Windows-only dependency and still ships only in `plugins/WinIO`,
so nothing changed about what a macOS bundle carries. The macOS bundle now has
`plugins/MacIO` holding one 24 KB assembly for both frameworks.

The pair now shares a load context. That is what makes it one plugin rather than
two in one folder, and it means a MIDI backend and a sound backend on the same
platform can see each other's types. Nothing does today. Where a platform
eventually needs it — a shared device-change notification, say, which is one
framework callback on macOS serving both — this is the arrangement that would
allow it without a third assembly for the pair to share.

What this does **not** do is fuse the two contracts. `IAudioOutput` and
`IMidiInput` are still separate, still discovered separately, and still selected
separately by priority. A backend that is not part of an operating system's
native pair — a portable PortAudio output, a network MIDI source — remains its
own plugin, which is exactly what the boundary in
[0025](0025-platform-io-behind-loadable-plugins.md) is for. The grouping decided
here is "these two ship and are supported as one", not "sound and MIDI are one
thing".

Should a platform's two halves ever stop being supported together — a Linux
machine with a sequencer and no PCM device would be the plausible case — the
plugin does not need splitting. `IsSupported` already answers per backend, and
the two are free to answer differently.
