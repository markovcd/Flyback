# ADR-0028: Publish one platform at a time, with only that platform's plugins

**Status:** Accepted · 2026-08-12

## Context

[0025](0025-platform-io-behind-loadable-plugins.md) put platform I/O behind
plugins loaded from disk and predicted the rest: a second backend would be a
project someone could add without touching the shell. It now exists.
`Flyback.Plugins.CoreAudio` is macOS sound — the default output audio unit,
reached by hand-written P/Invoke — and the shell did not change by a line to
gain it.

Shipping it did change things, because three faults in the build had been latent
while there was only one backend and only one platform anyone published for.

`Directory.Build.targets` built each declared plugin into `$(OutDir)plugins/`.
That is the *build* output. Publishing copies what the SDK knows about, and it
knows nothing about a folder a custom target wrote, so `dotnet publish` produced
an application with no `plugins/` directory at all — a program that starts,
draws, exports a WAV, and is silent, with a disabled button explaining that no
backend is installed.

Worse, publishing for a runtime identifier did not get that far. `-r osx-arm64`
sets `RuntimeIdentifier` as a global property, and global properties flow into
the `MSBuild` task that builds each plugin. The plugin project was restored
without that identifier, so the build stopped at `NETSDK1047`, several steps from
the publish that caused it and naming a project the person publishing had not
mentioned. **This was not a macOS problem.** `-r win-x64` failed identically; the
Windows application had simply never been published that way.

Third, every declared plugin was built into every output. Two backends make that
visible: a macOS application would carry NAudio's five assemblies in order to
answer "not supported", and a Windows one would carry CoreAudio to do the same.

And a fourth thing, which is not a fault but a fact: macOS does not run a folder
of files. It runs a bundle.

## Decision

**Plugins are built into the publish output too.** `PublishPlugins` is the
counterpart of `BuildPlugins`, hooked after `Publish`, writing into
`$(PublishDir)plugins/<FolderName>/` by the same mechanism — building into the
folder rather than copying into it, so the SDK decides which dependencies belong
beside a plugin. It clears the directory first, which the build target does not:
publishing twice into one folder is normal, and a plugin left over from a
previous run would be loaded as though it belonged.

**The plugin build is portable.** `RuntimeIdentifier` is removed from the
properties passed down, along with `SelfContained` and the publishing switches,
which would otherwise ask a plain class library to be trimmed, AOT-compiled or
made self-contained. Nothing shipped here has native assets, so portable is
correct as well as convenient; a plugin that did have them would declare its own
`RuntimeIdentifiers` and this decision would be revisited for that plugin alone.

**A plugin may name the one platform it is for.**

```xml
<PluginProject Include="..\Flyback.Plugins.Wasapi\Flyback.Plugins.Wasapi.csproj"
               FolderName="Wasapi" Platform="win" />
```

`Platform` is matched against the leading part of the runtime identifier — `win`,
`osx`, `linux` — falling back to the machine doing the building when there is no
identifier. A plugin that declares one is left out of output for anywhere else. A
plugin that declares none goes everywhere, which is what a module plugin such as
Supersaw or Space wants, since a module is not platform-specific at all.

**Publishing for an `osx-*` identifier lays out `Flyback.app`** beside the
publish folder, with the whole payload under `Contents/MacOS/`, the plugins with
it, and `Info.plist` and an icon around them. Beside rather than inside, because
a bundle within its own payload would copy itself; a copy rather than a move,
because the publish folder is what the caller asked for.

## Consequences

`dotnet publish -r <rid>` works, for `win-x64`, `win-arm64`, `osx-arm64`,
`osx-x64` and `linux-x64` — and the Windows one is fixed by the same change that
made the macOS one possible, having been broken in exactly the same way.

Each build carries one backend. The macOS bundle has `plugins/CoreAudio` and no
NAudio; the Windows publish has `plugins/Wasapi` and no CoreAudio. Because the
plugins are portable managed assemblies, the host loader can be pointed at either
folder from either platform, which is how both were checked without two machines:
the macOS bundle's plugins load on Windows, register, and report
`IsSupported: false`.

**A bundle cross-published from Windows is unfinished, and cannot be finished
there.** It is missing an executable bit, which a filesystem without file modes
cannot set, and a signature, which macOS on Apple silicon requires before it will
run anything at all. Both are one command on the Mac and both are printed by the
build that produced the bundle, because the alternative is discovering them as a
bundle that does nothing when double-clicked. Publishing on macOS sets the mode
and leaves only the signature.

Notarisation, `.dmg` packaging and a real signing identity are deliberately not
decided here. They need an Apple developer account and a Mac, and neither is a
property of this repository.

`tests/Flyback.Plugins.Tests` declares both backends **without** `Platform`, so
both are built into its output on every machine. That is not an oversight: what
those tests pin is that a plugin for another operating system still loads and
still answers, and leaving one out where it cannot play would delete the case
under test.
