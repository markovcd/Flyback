# An Android editor

Planned on 2026-10-06, on `main` at `7a65ff53`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last step.

- **Kind:** Plan
- **Status:** In progress. Steps 1 to 5 are done (ADR-0184); MIDI compiles against the bindings and has not been run on a device.

## What is wanted

`Flyback.Editor.Android`: the editor as an app on an Android tablet or phone, a third shell
beside `Flyback.Editor.Desktop` and `Flyback.Editor.Web`. Nothing here was run; it is read
from the code, except where the order says done.

## Where it stands

`src/Flyback.Editor.Android` boots to the canvas on an emulator, draws the picture on GLES
at 49 fps, and plays the sound through `Flyback.Plugins.AndroidIO`'s `AudioTrack`. The sound's
IL runs under Mono's JIT (see the speed below).

The toolchain is user-local: a Microsoft .NET SDK in `~/.dotnet`
(Ubuntu's packaged SDK takes no workloads) with the android workload, a JDK in
`~/Android/jdk`, the SDK in `~/Android/Sdk` and an AVD named `flyback` (Pixel Tablet,
API 36, x86_64). Build and install with `DOTNET_ROOT=~/.dotnet`, `JAVA_HOME` and
`ANDROID_HOME` set:
`dotnet build src/Flyback.Editor.Android -t:Install`. Never pass `-p:RuntimeIdentifier`: it
flows into every referenced project and rewrites their lock files. The build installs for
the attached device's ABI.

The emulator boots headless with `-no-window -gpu swiftshader_indirect -audio none`. With
host audio its PulseAudio driver fails to start and QEMU segfaults a few minutes into
playback; it also hangs and dies while a full test run has the machine loaded.

A launch opens a preset and picks the interpreter from the intent:
`adb shell am start -n app.flybackmodular.editor/crc64ce5bee3bb3e85030.MainActivity --es preset Sidebands --ez interpreted true`.
A broadcast asks the running editor how it is doing, and the answer is the JSON the web
editor's `window.flyback.state()` gives, read by the same `EditorReadout`: the preset, the
renderer and its rate, what the sound plays through, `soundRunsOn` (`IL` or `interpreter`),
`soundSpeed` (0 until IL plays) and the status bar's last message:
`adb shell am broadcast -a app.flybackmodular.editor.STATE -p app.flybackmodular.editor | sed -n 's/.*data="\(.*\)"$/\1/p'`.
Only a sender holding `DUMP` reaches it, which adb's shell does and another app does not.

Not yet checked on a device: MIDI, the state broadcast (compiled against the bindings only), the dialogs (`WindowDialog`), the file pickers, the gallery's
download from the preset site, and anything after a rotation.

### Speed on a phone

| Where | What | Speed |
|---|---|---|
| x86_64 emulator, Release | Sidebands, 2× oversampling | 18× real time |
| x86_64 emulator, Release | Whole band, no oversampling | 1.4 to 1.9× |
| Pixel 9 Pro (Tensor G4, ARM64, API 37), Release | Mycelium, no oversampling | 1.2× |
| i5-14600KF, Release `flyback-cli render` | Mycelium, no oversampling | about 13× |

The phone is about eleven times slower than a desktop, so Mycelium only just keeps up there
and the heavier presets will not. **The user accepted this speed for now (2026-10-08):** it
does not block the phone layout, Line In or MIDI.

The lead when it is taken up: .NET Android runs Mono's JIT, which generates weaker code than
the desktop's RyuJIT. .NET 10 is believed to carry an experimental CoreCLR runtime for Android
(`<UseMonoRuntime>false</UseMonoRuntime>`); unchecked whether it exists in this SDK or runs the
editor. Build with it, measure Mycelium on the phone again, and the difference is what the
runtime costs rather than the phone. The engine's own leads are in the `performance` skill.

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
4. **Sound.** `Flyback.Plugins.AndroidIO`, linked in, registers an `AudioTrack` output,
   written from a thread of its own as ALSA's is, an `AudioRecord` input and a `MidiManager`
   input, against `IAudioInput` and `IMidiInput`.
5. **The IL path.** The sound's desktop speed is IL generated at run time (ADR-0076). It runs
   under Mono's JIT. A Debug build runs Mono's interpreter by default, where
   `RuntimeFeature.IsDynamicCodeCompiled` is false and no IL is built, so the project sets
   `UseInterpreter=false`. On a Pixel 9 Pro it is about eleven times slower than a desktop.
6. **Trimming.** Release builds trim, and the patch reader deserializes by reflection: take
   the page's `TrimMode=partial`.
7. **Touch, the bulk.** ADR-0165 makes a finger a mouse button, and the touch bugs it left
   (the keyboard over the code view, wires lost to the socket snap, a two-finger pan that
   edits, sliders that change on a scroll, no keys on the screen for a MIDI In preset) are
   fixed. A tablet works; a phone needs a `ShellLayout` of its own, which is design, not
   plumbing.
8. **Packaging.** arm64-v8a, plus x86_64 for the emulator, as an APK or AAB. The Android
   signing keystore is a second secret and stays out of the repo (`security.md`);
   `release.sh` grows an Android step; no self-update, since the store or a sideload owns
   that. The site's list of platforms changes in the same commit (`website` skill).
9. **Driving it.** Test everything platform-free headless at a phone-sized window with
   `flyback-cli shot` (ADR-0166); only the GL context, the audio device and permissions need
   an emulator over `adb`. An intent opens a preset, and the state broadcast says what plays
   and how fast; what remains is a script that does both and waits for the sound to be timed.

## The order

1. ~~A spike: the app boots to the node canvas on an emulator.~~ Done.
2. ~~The picture on GLES.~~ Done: the desktop's `GpuPreviewSurface` draws as it is.
3. ~~Sound through `AudioTrack`, and the IL check.~~ Done, and measured on a Pixel 9 Pro.
4. ~~The touch bugs, then a tablet layout.~~ Done: the touch bugs a tablet meets are fixed, the
   desktop's layout holds in both orientations with the picture's row capped to its shape, and
   the splitters reach a fingertip either side. Three touch bugs remain, two of them the page's.
5. A phone layout, Line In and MIDI. The phone keeps the narrow mode every narrow window has
   (the user's choice, 2026-10-08): the canvas, or the picture and the inspector in its place
   under the toolbar's Side button. A patch opened on a phone was framed at the canvas's first,
   passing size and left small in a corner; a view nothing has moved is now framed again on
   every resize. Checked on a Pixel 9 Pro emulator (`flyback-phone`), upright and sideways.
   Line In is done: `Flyback.Plugins.AndroidIO` records through `AudioRecord`, unprocessed
   where the device allows, stereo where the microphone has it, after a transparent
   `MicrophoneActivity` has put Android's question; a refusal leaves the Line In silent and
   says so. Checked on a Pixel 9 Pro with the Visualizer preset. MIDI is written:
   `MidiManagerInput` lists what `android.media.midi` has over USB or from another app, opens
   a device through Android's listener and hears its bytes through `MidiStream`, the stream
   reader in `src/plugins/Shared/Midi` that MacIO reads its packets through too, since
   Android hands over a raw stream where Windows and ALSA get a message at a time. Compiled against the bindings and the SDK's
   analyzers; not run on a device. A Bluetooth keyboard is left out, since Android lists one
   only once an app has opened it by address.

The ADR is ADR-0184.
