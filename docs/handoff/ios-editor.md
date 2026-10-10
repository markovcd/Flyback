# An iPhone and iPad editor

Planned on 2026-10-06, on `main` at `7a65ff53`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

`Flyback.Editor.iOS`: the editor as an app on an iPhone or iPad, a shell beside the desktop,
the page and, once it lands, Android ([android-editor.md](android-editor.md)). Most of that
plan holds here, and the touch bugs and the phone layout are the same work, done once for
both. Nothing here was run; it is read from the code and from what iOS is known to forbid.

## What is different from Android

1. **No JIT.** iOS runs ahead-of-time code only, so the IL the sound plays from (ADR-0076)
   cannot be generated, as in a browser (ADR-0160). The interpreter plays: the desktop
   interpreter's speed, which is the baseline the browser's numbers are quoted against. A heavy
   preset that needs the IL will not keep up, and the worker's step-down rule (ADR-0168) is
   the likely answer. Unchecked on a device.
2. **No OpenGL to lean on.** Avalonia.iOS draws through Metal, and OpenGL ES is deprecated.
   Whether `OpenGlControlBase`, and so `GpuPreviewSurface`, gets a context at all is not
   known; check it first. If not, the picture needs another way: the interpreter's own
   CPU render, ES 3.0 over a Metal layer (ANGLE), or a Metal backend beside `GlslEmitter`,
   the last of which is a project of its own.
3. **A Mac to build on.** `net10.0-ios` needs the `ios` workload, Xcode and a Mac. The
   development machine is Linux, so a session cannot build or run it here. The build is a
   Mac runner kept out of the gate, and the emulator is the iOS Simulator, driven with
   `xcrun simctl`; the headless shot (ADR-0166) still tests everything that is not the GL
   context, the audio session or a permission.
4. **Sound.** An `AudioSetup` over `AVAudioEngine` or a RemoteIO audio unit, with an
   `AVAudioSession` category chosen so the silent switch and other apps' playback behave.
   Line In needs `NSMicrophoneUsageDescription`; MIDI is CoreMIDI, which `Flyback.Plugins.MacIO`
   already speaks on a Mac and may share.
5. **Keys.** `Flyback.Plugins.Keychain` is the macOS secret store (ADR-0034); iOS has the same
   Security framework, so the assistant's key may reuse it.
6. **Plugins.** As on Android, linked in: nothing loads a folder of assemblies, and an app
   store forbids fetching new code besides.
7. **Shipping.** An Apple developer account, a signing certificate and a provisioning profile,
   all secrets outside the repo (`security.md`); TestFlight, then the App Store with its review
   (a synthesizer that runs patches is data, not downloaded code, but say so in review). No
   self-update. The site's platform list changes in the same commit (`website` skill).

## Does it earn its place

The web editor already runs on a phone, an iPhone included. What a native app adds is sound without the browser's
gating, MIDI and Line In, files in the Files app, and an icon on the home screen. Settle that
before spending a Mac runner on it.

## The order

1. Fix the touch bugs and design the phone layout; both are Android's work too.
2. A spike on a Mac: the app boots to the node canvas in the Simulator, no sound, no picture.
3. Find out whether the picture gets a GL context; pick its way from item 2 above.
4. Sound through `AVAudioEngine`, measured on a device with the interpreter alone.

An ADR comes first: the shell cannot load plugins from a folder, cannot generate code, and may
not have OpenGL.
