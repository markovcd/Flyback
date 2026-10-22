# ADR-0211: The editor builds for iOS as it does for Android

**Status:** Accepted · 2026-10-22 · *user-directed* · follows
[0184](0184-the-editor-runs-on-android-with-its-plugins-linked-in.md); implemented in
`src/Flyback.Editor.iOS`

## Context

The user asked whether an iOS app could come from the same code as the Android one. Avalonia
has an iOS head, and Core, Engine and `Flyback.Editor` hold no platform (0184). Three things
differ from Android: iOS runs no code it did not ship with, so neither a plugin folder nor a JIT
is allowed on a device; the toolchain is Xcode and the ios workload, which need a Mac; and the
App Store needs a paid developer account, which the project does not have.

## Decision

**A head of its own, shaped like Android's.** `Flyback.Editor.iOS` is an `AvaloniaAppDelegate`
and a `DeviceApp` holding the same `EditorView`, with the module plugins in `LinkedPlugins.props`
linked in. Settings, presets and recovery live in the app's Application Support folder.

**Sound through the macOS plugin.** `Flyback.Plugins.MacIO` is linked in, and on iOS its
CoreAudio output opens the RemoteIO unit instead of the default output unit; there is no device
to choose. Line In and MIDI stay macOS-only until something needs them on iOS.

**Ahead of time, with the interpreter behind it.** A device build compiles everything ahead of
time and keeps Mono's interpreter for what reflection asks for at run time
(`MtouchInterpreter=-all`). The sound's IL is never built there, so the engine interprets the
patch, as it does in a browser. The simulator has a JIT, so `--interpreted` is what tries a
device's path there.

**The picture on OpenGL ES.** Avalonia's iOS head starts on EAGL, where `GpuPreviewSurface`
runs as on Android; Metal is asked for only if it fails.

**Driven by a script.** The app takes `--preset` and `--interpreted` at launch and answers the
Darwin notifications `app.flybackmodular.editor.STATE`, `PLAY` and `SIDE` by writing its
`EditorReadout` to `readout.json`. `scripts/ios-smoke.sh` builds, boots a simulator, plays a
preset and checks the answer.

**Built by path, by hand.** `Flyback.slnx` lists the project without building it, as Android's.
`ios.yml` builds it and runs the smoke on a Mac when started by hand, since a macOS minute costs
ten times a Linux one (0200). Nothing is signed or published.

## Consequences

- A device plays only what the interpreter keeps up with; a heavy preset will not.
- Publishing needs an Apple Developer account, a signing certificate and a provisioning
  profile, none of which exist yet.
