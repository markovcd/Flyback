# Blocking work on the UI thread, and what to do about each

Audited on 2026-09-29, on `main` at `f1756249`. The question was which synchronous
methods can and should become async. There is no sync-over-async that could
deadlock: the six `.Result`/`.Wait()`/`GetResult()` in `src/` are a finished task
(`Reactions.Raise`), pipes read after the process exited (`LoginKeychain`,
`SecretTool`, `PackPluginCommand`, `Mp3Reader`) and the close at shutdown
(`TakeRecording.FinishNow`). What is left is synchronous work on the UI thread long
enough to feel, and most of it is fixed by a cache, a `Task.Run` or loading ahead,
not by an async signature. It is on TODO.md; take it off there, and delete this
file, in the commit that lands the last item.

## Fix

Ranked by how much a user feels it. 1, 3, 4 and 5 are one small commit with no
contract change; 2 wants its own.

### 1. Assistant key lookups start a process per keystroke

`AssistantPanel.Refresh()` (`src/Flyback.App/Assist/AssistantPanel.cs:901`) runs
from the constructor and on every `form.Changed` (line 410). It reaches
`ISecretStore.Recall` about four times, uncached: `credentials.Transport` from
`Configured()` (line 893), `SourceOf` (1035) and `HasEntered` twice (1045, 1071),
each through `Credentials.FromStore` (`src/Flyback.Plugins/Assist/Credentials.cs:193`).
On macOS and Linux each Recall starts `security` or `secret-tool` and waits up to
30 s if the keyring asks to be unlocked (`LoginKeychain.cs:136-153`,
`SecretTool.cs:195-222`); on Windows DPAPI reads one file.

Cache what `FromStore` recalled per account inside `Credentials`, cleared by
`Accept` (126) and `Forget` (147). `SourceOf` compares the session key against a
read-back of the store on purpose (ADR-0034): the read-back after `Accept` must
still hit the store once, so clear before reading, not after. Do not make
`ISecretStore` async: under ADR-0102 that is a default-bodied member and a minor
bump, and the cache removes the calls rather than moving them.

### 2. A sample is decoded inside the compile

`Playback.Recompile` (`src/Flyback.App/Playback.cs:241`) compiles on the UI thread
by design (engineering guide §6, ADR-0018). The first compile that names a sample
reaches `SampleLibrary.Look` (`src/Flyback.Engine/Render/SampleLibrary.cs:112`),
which reads a WAV whole (up to `WavReader.MostSamples`, ten minutes) or runs ffmpeg
for an MP3 and waits for it (`Mp3Reader.cs:110-127`). `ImageLibrary` loads PNGs
the same way. Hundreds of milliseconds to seconds, on open, on `FilesMoved`, and
on the edit that first names a file; cached per path after.

Warm both libraries for the patch's paths off the UI thread when a patch opens
or a path appears, then compile. `ISampleLibrary.Find` and `IImageLibrary` are
Core's shipped contract and stay synchronous.

### 3. Plugin install unpacks on the UI thread

`PluginInstalls` (`src/Flyback.App/PluginPackages/PluginInstalls.cs:181`) calls
`installer.Stage(package, platform)` on the UI thread: it unpacks up to
`PackageLimits.Unpacked` (512 MB) and `PluginFiles.Of` hashes every file.
`PluginPackage.ReadAsync` ends in a synchronous `Read(memory.ToArray(), limits)`
(`src/Flyback.Plugins/Hosting/PluginPackage.cs:224`) that parses the zip on the
continuation (callers at `PluginInstalls.cs:140` and `:257`). Put the Read and the
Stage in `Task.Run`.

### 4. Saving a bundle zips on the UI thread

`PatchFiles.SaveBundleAsync` (`src/Flyback.App/PatchFiles.cs:320`) is async only
for the final copy: `PatchBundle.Write` reads every carried file through `Bytes`
and deflates it first. `PresetSlot.Keep` (`src/Flyback.App/Gallery/PresetSlot.cs`)
→ `PresetLibrary.Save` does the same, then `WriteAllBytes` and `Reload`. The patch
is an immutable snapshot by then, so the Write goes in `Task.Run`; the
conversation is taken (`ConversationToSave()`) before it, on the UI thread.

### 5. Linux file types wait on two processes

The Settings Save click → `FilesSection.Save` (`src/Flyback.App/Files/FilesSection.cs:93`)
→ `LinuxFileTypes.Apply` → `Run` (`LinuxFileTypes.cs:119-127`) runs
`update-mime-database` and `update-desktop-database`, each with
`WaitForExit(10_000)`: usually under a second, twenty at worst. This one is a real
async conversion: `WaitForExitAsync`, and `FileTypes.Apply` (`FileTypes.cs:25`)
returns a `Task` in all three platforms.

## Could, but gains little

- `PatchFiles.OpenBundleAsync` (line 544) and `SavedPreset.Open`
  (`src/Flyback.Ui/SavedPreset.cs:45`, from `PresetSlot.Arrive`) inflate the zip on
  the UI thread, but a UI-thread compile follows either way.
- `ModulePalette.Reset` → `GroupLibrary.Reload` parses every saved group each time
  the palette opens. Small files, unbounded count.
- `TakeRecording.Start` → `LiveRecorder` → `ClipWriter.Open` starts ffmpeg, and
  `Ffmpeg.Resolve` scans PATH. Tens of ms; `Process.Start` has no async form.
- `ConversationStore.Keep`/`Find`: one JSON file that grows with the chat.
- The server's `MediaFolder` (`src/Flyback.Server/MediaFolder.cs:20-49`) does up to
  six `File.Exists` and a peaks read per preset on every `GET /presets`, and
  `?pending=true` walks every published preset. Cache the media state; async does
  not help, since `File.Exists` has none.

## Ruled out

- The server's SQLite stores (`PresetStore`, `PluginStore`, `ReportStore`,
  `RatingStore`, `LetterStore`): Microsoft.Data.Sqlite's `*Async` runs synchronously.
  The real cost is `Download`/`Preview` loading a whole blob (up to 20 MB) into a
  `byte[]`; `SqliteBlob` streaming if it ever matters.
- Kestrel uploads already use `ReadFormAsync`/`CopyToAsync`; `AllowSynchronousIO`
  is never needed.
- The assistant contract: `IAssistantTransport.Send`, `IModelConversation.Send`
  and `IModelSurvey.Survey` already return `Task`, and Gemini and OpenAi await.
- The CLI: `RenderPresetsCommand` is async and sequential on purpose; `ProbeCommand`
  probes in order so its output is ordered; `ViewerCommand` waiting on its child
  is the point.
- Before the window exists: `Restart.Awaited`, `Updater.HandOff`/`Apply` with
  Installer's retries, `Startup.Load` (`UpdateFolder.Tidy`,
  `PluginInstaller.Finish`), the Viewer's open, the server's `Defaults`.
- Already off the UI thread: `ThumbnailStore`, `PresetThumbnails`,
  `UpdateDownloader`, `WorkKeeper` ticks, `LiveRecorder`'s worker, `PresetAudition`,
  the ffmpeg version probe in `OutputSections`, the engine's writers.
- `Aptabase.Drain`: a bounded `WaitAll` on exit or crash (ADR-0103).
- `AudioEngine`: the callback is lock-free; opening and closing the device are
  native calls with no async form.
- Settings files (`WindowLayout`, every `*Settings`, `InstrumentLibrary`): tiny,
  read at startup, on close or on a dialog's Save.
