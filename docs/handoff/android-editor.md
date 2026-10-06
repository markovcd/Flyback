# An Android editor

Planned on 2026-10-06, on `main` at `7a65ff53`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

`Flyback.Editor.Android`: the editor as an app on an Android tablet or phone, a third shell
beside `Flyback.Editor.Desktop` and `Flyback.Editor.Web`. Nothing here was run; it is read
from the code.

## What already carries over

- Core and Engine are plain `net10.0`, so the patch, the compiler and the interpreter run
  unchanged.
- `Flyback.Editor` has no platform in it (ADR-0150, ADR-0162). The web shell, about 1,400
  lines, is the template: an `Application` that builds `EditorServices.Provider(...)` and
  overrides the sound, title, focus and close.
- The picture is probably close. `GpuPreviewSurface` already reads `GlProfileType.OpenGLES`
  into a `ContextVersion`, and `GlslEmitter` has a `GlslEs300` dialect.

## What it takes

1. **Toolchain.** `dotnet workload install android`, a JDK and the Android SDK; none is on
   the development machine. A `net10.0-android` project taking `Avalonia.Android` 12.1.3
   through `Directory.Packages.props`. Build it outside the Docker gate, as `EditorWeb=false`
   and ADR-0028 already treat a platform, or the gate image grows an SDK.
2. **The shell.** An `AvaloniaMainActivity<App>`, an `App` modeled on `PageApp`, and
   app-private folders for settings and presets. `EditorHost.InPage` is the wrong flag: it
   also hides open and save, settings, plugins and the assistant. Android wants a host shape
   that opens and saves through the system picker (Avalonia's `StorageProvider`).
3. **Plugins.** The folder and `AssemblyLoadContext` loader (ADR-0025) does not work from an
   APK; link the module plugins in with `PluginHost.LoadLinked`, as the page does. Installing
   a `.fbkp` is off. The CLI-wrapping assistant plugins (Claude Code, Codex) cannot run;
   OpenAi and Gemini could, once a keystore plugin holds the key (ADR-0158).
4. **Sound.** The new code. An `AudioSetup` over `AudioTrack` or AAudio, linked in like the
   page's `PageSpeakers`. Line In (`AudioRecord`, `RECORD_AUDIO`) and MIDI
   (`android.media.midi`) follow as later plugins against `IAudioInput` and the MIDI
   interface.
5. **The IL path.** The sound's desktop speed is IL generated at run time (ADR-0076). .NET on
   Android runs Mono with its JIT, so it should work, but it is unchecked, as is ARM64 speed
   on a real phone. Without it the interpreter plays, five times slower per op (ADR-0160).
6. **Trimming.** Release builds trim, and the patch reader deserializes by reflection: take
   the page's `TrimMode=partial`.
7. **Touch, the bulk.** ADR-0165 makes a finger a mouse button, but
   [touch-bugs.md](touch-bugs.md) has twelve open: the keyboard over the code view, wires
   lost to the socket snap, a two-finger pan that edits, sliders that change on a scroll, no
   on-screen keyboard for a MIDI In preset. Land those first. A tablet then works; a phone
   needs a `ShellLayout` of its own, which is design, not plumbing.
8. **Packaging.** arm64-v8a, plus x86_64 for the emulator, as an APK or AAB. The Android
   signing keystore is a second secret and stays out of the repo (`security.md`);
   `release.sh` grows an Android step; no self-update, since the store or a sideload owns
   that. The site's list of platforms changes in the same commit (`website` skill).
9. **Driving it.** Test everything platform-free headless at a phone-sized window with
   `flyback-cli shot` (ADR-0166); only the GL context, the audio device and permissions need
   an emulator over `adb`. The friction to remove first is a command that launches the
   activity on a preset and returns `logcat` and the meters' readings, so a session can
   confirm sound without ears.

## The order

1. A spike: the app boots to the node canvas on an emulator, no sound, no picture. It
   answers whether Avalonia 12 on Android and the editor get along.
2. The picture on GLES.
3. Sound through `AudioTrack`, and the IL check on a device.
4. The touch bugs, then a tablet layout.
5. A phone layout, Line In and MIDI.

An ADR comes first: Avalonia stays (ADR-0015), and what is new is a shell that cannot load
plugins from a folder.
