# Where the architecture is weakest, and the refactors that would fix it

Written on 2026-10-08, on `main` at `a00af7c`. An audit, not a plan: each item is
a proposal, and goes to TODO.md only once the user picks it. Delete an item here
in the commit that lands it, and the file when the last goes.

Code lines are `grep -cvE '^\s*(//|$)'`; comments run a third to two thirds of
most files here. Everything was confirmed by reading; nothing was run.

What holds and is not raised again: the layers reference one way; Engine takes no
packages; Core references nothing; the container is used uniformly across the
three editor hosts, with no hand-built service outside composition; every public
*type* in Core and Plugins is named by a plugin; the backends are transcriptions
by decision (ADR-0035); the binder is one walk (ADR-0183); presets are C# by
decision (ADR-0138).

## 4. `Document` is the editor's god object (Medium)

`src/Flyback.Editor/Document.cs` is 657 code lines with 30 public members
reached from 20 files, holding seven concerns: ownership transitions
(`:1236-1421`), the undo landing (`:794-1032`, ADR-0071), the knob-to-text
write-back with five pending sets `turned/restated/dialed/given/relaid`
(`:482-771`), caret-follows-selection with `adrift` flags (`:385-441`), paste of
JSON into text (`:451-479`), view toggling and printing (`:1044-1137`), and
evaluation (`:1150-1221`). The `writingBack/stepping/mapped/printed` guards are
shared by all seven, so every feature touching text or knobs edits this file.
ADR-0148 gives Document ownership, write-back and the undo landing; it does not
forbid helpers.

**Fix.** `TextWriteBack` (the five sets and WriteBack/Write/Carry/Rest/Lay/Put/
PanelEdited), `CaretFollow` (PointAt/Adrift, raising PanelStale) and a static
`PastedPatch.Written`, each a part in the container. Document keeps ownership,
the undo landing, Evaluate and ShowCode, about 350 lines.

## 5. `PresetGallery` is six partials around four nested classes (Medium)

`Gallery/PresetGallery.cs` 473, `.Layout.cs` 442 (nested `Layout`), `.Site.cs`
244 (`SiteRun`), `.Choice.cs` 242 (`Choice`), `.Prompt.cs` 66, `.Card.cs` 43
(`Card`): 1,510 code lines of one type, beside `PresetSlot.cs` (475) that is
"everything the gallery it opens does". It is the ADR-0039 shape ("a banner is
not a boundary") that ADR-0148 replaced for the window, kept alive through the
one-type-per-file rule's nested-type allowance. The same loophole shows in
`Canvas/CanvasGestures.cs` (613, "five gestures over one Drag state").

**Fix.** Promote `Layout`, `Choice`, `SiteRun` and `Card` to top-level
`GalleryLayout`, `GalleryChoice`, `SiteRun`, `PresetCard` in `Gallery/`,
registered with `AddPart`, and delete the partials. Split `CanvasGestures` per
gesture (`ModuleDrag`, `WireDrag`, `BoxSelect`, `PanZoom`, `WheelTurn`) over
the `Drag` record it already names.

## 6. Public members only the host calls, one of them a hole (Medium)

`ContractSurfaceTests` guards types; nothing guards members. Public in Core with
no plugin naming them: `NodeCatalog.BusOf` (`Buses.cs:24`), `FormulaOf` and
`FormulaProblem` (`Maths.cs:375,383`), `IsChart` (`Output.cs:34`),
`LegacyTypeIds` (`Legacy.cs:18`), and `NodeCatalog.Install`, which a plugin
could call to swap the catalog under the host. In Plugins:
`PatchWorkbench.Undescribed:107`, `.Edits:142`, `.ToolCalls:145`,
`ToolOutcome.Fine/Reference`, `AssistantConfig.Unset`; ADR-0102 says the
workbench's public members are the ones an assistant calls. Housekeeping:
`Flyback.Plugins.csproj` has `<Compile Update="Assist\PatchWorkbench.Senses.cs">`
for a file that no longer exists.

**Fix.** `internal` with `*REMOVED*` lines in `PublicAPI.Unshipped.txt`
(`InternalsVisibleTo` already covers the hosts), and a member-level pass in
`ContractSurfaceTests` over `PublicAPI.Shipped.txt` against `src/plugins` and
the two test plugins, so the next host-only member fails the gate.

## 7. A tool's argument names are declared twice (Medium)

`src/Flyback.Plugins/Assist/ToolTable.cs:64-477` declares sixteen tools with a
JSON schema string each; the bodies re-spell the property names as literals
(`ModuleEdits.cs:24,41` and 17 more reads through `ToolArguments`). Nothing ties
a schema's properties to what its body reads, so a rename drifts silently and
`ToolOutcome.Refused` is the only symptom.

**Fix.** A `ToolField(Name, Kind, Required, Description)` record declared once
per tool; `PatchTool.Schema` is rendered from it and `ToolArguments` reads
through it. Cheaper first step: a test asserting every property a body reads is
in its tool's schema.

## 8. Startup has three homes and a bag of statics (Medium)

Registration is one `AddPart<T>` and the graph is validated, so the container
holds (ADR-0150). What sprawls is *when* things run: `EditorView.cs:377-382`
starts every `ISettingsSection` from the constructor while `Reactions.building`
is true, so a section's `Start` cannot raise a notice, against ADR-0148's "say
it from Start"; `EditorStart.cs:27-45` runs nine steps from `MainWindow.Start`;
`EditorOpened.cs:15-22` runs three more from `Window.Opened`. A new startup step
has three candidate homes. `Editor.Desktop/Startup.cs:20-79` exposes ten static
settable properties copied field by field into `EditorLaunch` at
`FlybackApp.cs:58-68`. `EditState.cs:57-80` rebuilds the inspector and sets
toolbar buttons from a fourth class.

**Fix.** `IStartAt { Phase; Task On(); }` collected by `Parts` with phases
Built, Shown, Opened; `EditorStart` and `EditorOpened` become ordinary parts;
`Startup.Load` returns an `EditorLaunch` instead of statics; Inspector and
Toolbar react to `OwnershipChanged` themselves.

## 9. One feature written twice, smaller (Medium to Low)

- **MIDI learn**: `Knobs/PanelKnobs.cs:388-396` and `Knobs/KnobRandomizer.cs:200-208`, same device list, cancel-and-replace and "no MIDI device" report. A `MidiLearn` part both call. `ControlsPanel.cs` carries five events that exist only to forward `RollCell` to `KnobRandomizer`.
- **Stage keys**: Escape, F3, F11, Space and Ctrl+P handled in `EditorView.cs:166-178`, `Windows/PictureWindow.cs:64-66` and `Viewer.Desktop/ViewerWindow.cs:131-138`. A `StageKeys` map in Ui all three ask. `TransportControls.cs:44,50` takes `Overlay` and `PictureWindow` as public settable properties; `Register(...)` instead.
- **The model survey loop**: `OpenAi/OpenAiProbe.cs:49-95` and `Gemini/GeminiSurvey.cs:61-117` are one loop (bare, picture, sound, report). ADR-0161 pulled the turn into the host and left the survey out; `GeminiSession.cs` declines sharing the *session* loop, not this. `Assist/SurveyLoop.cs` over an `IModelProbe`.
- **The audio writer thread**: the same thread, block, `volatile running`, `Stop` with `Join(2000)` in `LinuxIO/AlsaAudioDevice.cs:50-148`, `AndroidIO/AudioTrackDevice.cs:16-120`, `LinuxIO/AlsaAudioCapture.cs:37-160`, `AndroidIO/AudioRecordCapture.cs:16-97`. `src/plugins/Shared/Audio/BlockWriter.cs` and `BlockReader.cs`, linked by source as `Shared/Programs` is. WASAPI and CoreAudio are callback-driven and stay as they are.
- **The page end of the speaker in JS**: `Viewer.Web/wwwroot/main.js:149-235` and `Editor.Web/wwwroot/speakers.js:73-120` do the same handshake with `speaker.js` and `sound.js`. One `speakers.js` exporting start/seek/time/status, imported by both pages as `gl.js` is.
- **CLI and Site**: `Flyback.Cli.csproj:34` links `Site/Admin/SiteAdmin.cs` as source, and the media PUT and `--server` check are still written twice (`Cli/Rendering/MediaUpload.cs:25-30` vs `Site/Commands/PushMediaCommand.cs:95-100`; `RenderPresetsCommand.cs:95-100` vs `SiteAdmin.Client:16`). A `Flyback.Site.Client` library both reference. Check while there: `Flyback.Site.csproj:24-33` lists the web plugins twice, minus Drawings, and `LoadLinked(..., "WebPlugin")` names Drawings; if `Assembly.Load` fails there, a Drawings preset is reported as lacking. Not confirmed by running.
- **Silent sound stand-ins**: `Editor.Desktop/Shots/ShotSound.cs` and `Editor.Web/PageSound.cs` share the compile-for-live-inputs update and the null audition. An `UnplayedSound` base in Ui.
- **`PluginHost.cs`** (514 lines) is half a registry: three entry points, id-clash refusal, catalog assembly, and a nested `Registry` with checkpoint and rollback. `Hosting/PluginRegistry.cs` and `Hosting/PluginCatalogBuilder.cs`.
- **`CompiledPatch`** (406) is the program description, the interpreter, the IL hand-over and the arithmetic library. `Arithmetic` and `Interpreter` beside it; ADR-0076's "IL calls the interpreter's own helpers" survives the move.
- **Hand-kept registries**: `NodeCatalog.cs:38-60` concatenates 21 group methods by hand across 24 partials, and `Presets.cs:24-120` lists 26 builders; a new group or preset left off compiles and ships nothing. One reflection theory each.

## 10. Tests (Medium to Low)

- **Two headless harnesses for one editor.** `Flyback.Specs` does not reference `Flyback.Ui.Testing`; it carries a third `Application` subclass (`Support/Headless.cs`), its own session, and `Support/EditorDriver.cs` (906 code lines) and `Support/ViewerRun.cs` each re-implement `Settle()` and `Press()`. A fix to how a window settles lands in one harness and not the other. Specs references Ui.Testing and the drivers build on `UiTest`.
- **`Editor.Tests/Ui/` is a bucket.** 120 of 170 files sit in a folder `src/Flyback.Editor` does not have; the guide says the namespace mirrors the folder. Move only, into the feature folders.
- **Four files hold a fifth of Editor.Tests.** `AssistantPanelTests.cs` 1,186 code lines, `SourceViewTests.cs` 1,154, `OutputSettingsTests.cs` 943, `NodeEditorTests.cs` 676; `AssistantPanelTests` and `CredentialsTests` each carry a private `FakeStore : ISecretStore`. Split by rule group; one `FakeSecretStore`.

## 11. Drift in the documents, no code change (Low)

- ADR-0017's body says "about 2 700 across seven files"; `Canvas/` is 5,232 code lines in 42 files, `NodeEditor.cs` itself 211. The decision holds; the count in the latest amendment does not.
- ADR-0148 says `MainWindow` owns layout and keys in one file; ADR-0162 moved them to `EditorView.cs`, and neither cites the other. A dated amendment on 0148 and a README cross-reference.
- ADR-0035 and the guide's section 4 place `GlslEmitter` in `Core/Compile`; it is `Engine/Compile/GlslEmitter.cs`.
- `src/Flyback.Editor/IDialog.cs` sits at the project root and declares `namespace Flyback.Editor.Controls`, the one exception to "no exceptions".
- Eight `src/` files declare two top-level types, all in plugins: `Mastering/Dsp.cs`, `Figures/Strike.cs`, `Gemini/GeminiSurvey.cs`, `MacIO/CoreMidiInput.cs`, `LinuxIO/AlsaMidiInput.cs`, and the three secret-store plugins' `*Plugin.cs`. Split as each is next touched, per the rule.
- `Flyback.Ui` holds about 640 code lines nothing but the editor names (`Midi/InstrumentLibrary.cs`, `Audio/LineIn.cs`, `Controls/SeekTrack.cs`, `PlayheadLine.cs`, `FrameRateMeter.cs`, `PopupHoles.cs`, `FingerSwipe.cs`, `RandomizeSettings.cs`, `StatusClock.cs`, `MonitorSpot.cs`, `FullScreenOn.cs`, `OversamplingText.cs`, `GraphicsDriver(s).cs`, `GraphicsApi.cs`, `IPresetFolder.cs`, `Capture/IAudioSink.cs`), and `Midi/KeyCodes.cs` and `GraphicsDrivers.cs` are named only by tests. Item 1 decides where these go; until then, move them into the editor's feature folders and amend ADR-0124's list.

## Order

4 and 5 first, since the editor's churn lands in them. 6 before 1.0.0, since the
contract is a promise from then on. The rest as each file is next touched.

Landed: the host code that is not Avalonia has a home, `Flyback.Host`
(ADR-0188), and with it one plugin bootstrap, one bytes-to-patch reader, one
settings reader for the CLI, one playback registration and one preset finder.
The infix bracket rule is `Infix` in Core, which the binder, the printer and the
fusing all spell a sum through; the three `Strength` tables' negative-number
arms turned out never to change a spelling, and went with them. `OpShape` names
every opcode, refuses one it does not, and owns the "registers an op reads" loop
and the line-or-cell question for every pass and backend.
Still written per shell, and small: the speaker handshake in two JS files, the
viewer's and the CLI's `--size` and `--oversample` option declarations, and the
viewer's `--cpu` against the CLI's `--processor`.
