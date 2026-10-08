# ADR-0184: The editor runs on Android, with its plugins linked in

**Status:** Accepted · 2026-10-07 · *user-directed* · builds on
[0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md),
[0015](0015-avalonia-for-the-ui-shell.md) and
[0025](0025-platform-io-behind-loadable-plugins.md)

## Context

Core and Engine are plain `net10.0` and `Flyback.Editor` holds no platform (0162), so a
third shell is mostly a host. Two things differ from the desktop: an APK cannot load an
assembly from a folder it unpacks later, and its toolchain (the android workload, a JDK,
the Android SDK) is not in the gate image.

A spike on an Android 16 tablet emulator (x86_64, software GL) answered the open question:
the editor boots to the canvas on a shipped preset, and `GpuPreviewSurface` gets an
OpenGL ES context from Avalonia and draws the picture at 49 frames a second, with no
surface of Android's own.

## Decision

**Avalonia stays.** `Flyback.Editor.Android` is an `AvaloniaMainActivity` and a `DeviceApp`
modeled on `PageApp`, holding the same `EditorView` as a single view.

**Plugins are linked in, as in a page.** The module plugins in `LinkedPlugins.props` are
project references loaded by `PluginHost.LoadLinked`. The folder loader (0025) is not
used, a `.fbkp` does not install, and the plugins that wrap a CLI do not ship.

**The picture is the desktop's.** `GpuPreviewSurface` runs on GLES as it is; the page's
canvas surface (0162) is not needed.

**It keeps its files in the app's private folder.** Settings, presets, groups, thumbnails
and recovery live under `FilesDir`, which no other app reads.

**It is built by path, outside the gate.** `Flyback.slnx` lists the project with
`<Build Project="false" />`, so an IDE shows it while the gate neither restores nor builds it
and needs no workload. The android workload goes on a .NET SDK
from Microsoft, since a distribution's packaged SDK takes no workloads.

## Consequences

- Sound, Line In and MIDI on Android are linked-in backends still to write; until then the
  editor is silent.
- Whether the IL path runs under Mono's JIT on ARM64, and how fast, is measured on a device
  before sound ships.
- A phone needs a layout of its own; a tablet takes the desktop's once the touch bugs land.
