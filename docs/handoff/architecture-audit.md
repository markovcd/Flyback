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

## 10. Tests (Medium to Low)

- **Four files hold a fifth of Editor.Tests.** `AssistantPanelTests.cs` 1,186 code lines, `SourceViewTests.cs` 1,154, `OutputSettingsTests.cs` 943, `NodeEditorTests.cs` 676; `AssistantPanelTests` and `CredentialsTests` each carry a private `FakeStore : ISecretStore`. Split by rule group; one `FakeSecretStore`.

## 11. Drift in the documents, no code change (Low)

- ADR-0017's body says "about 2 700 across seven files"; `Canvas/` is 5,232 code lines in 42 files, `NodeEditor.cs` itself 211. The decision holds; the count in the latest amendment does not.
- ADR-0148 says `MainWindow` owns layout and keys in one file; ADR-0162 moved them to `EditorView.cs`, and neither cites the other. A dated amendment on 0148 and a README cross-reference.
- ADR-0035 and the guide's section 4 place `GlslEmitter` in `Core/Compile`; it is `Engine/Compile/GlslEmitter.cs`.
- `src/Flyback.Editor/IDialog.cs` sits at the project root and declares `namespace Flyback.Editor.Controls`, the one exception to "no exceptions".
- Eight `src/` files declare two top-level types, all in plugins: `Mastering/Dsp.cs`, `Figures/Strike.cs`, `Gemini/GeminiSurvey.cs`, `MacIO/CoreMidiInput.cs`, `LinuxIO/AlsaMidiInput.cs`, and the three secret-store plugins' `*Plugin.cs`. Split as each is next touched, per the rule.
- `Flyback.Ui` holds about 640 code lines nothing but the editor names (`Midi/InstrumentLibrary.cs`, `Audio/LineIn.cs`, `Controls/SeekTrack.cs`, `PlayheadLine.cs`, `FrameRateMeter.cs`, `PopupHoles.cs`, `FingerSwipe.cs`, `RandomizeSettings.cs`, `StatusClock.cs`, `MonitorSpot.cs`, `FullScreenOn.cs`, `OversamplingText.cs`, `GraphicsDriver(s).cs`, `GraphicsApi.cs`, `IPresetFolder.cs`, `Capture/IAudioSink.cs`), and `Midi/KeyCodes.cs` and `GraphicsDrivers.cs` are named only by tests. Item 1 decides where these go; until then, move them into the editor's feature folders and amend ADR-0124's list.

## Order

Each as its file is next touched.

Landed: the host code that is not Avalonia has a home, `Flyback.Host`
(ADR-0188), and with it one plugin bootstrap, one bytes-to-patch reader, one
settings reader for the CLI, one playback registration and one preset finder.
The infix bracket rule is `Infix` in Core, which the binder, the printer and the
fusing all spell a sum through; the three `Strength` tables' negative-number
arms turned out never to change a spelling, and went with them. `OpShape` names
every opcode, refuses one it does not, and owns the "registers an op reads" loop
and the line-or-cell question for every pass and backend. The preset gallery's
parts are top-level types rather than partials of `PresetGallery`, and the canvas's
gestures are `ModuleDrag`, `WireDrag` and `RubberBand` under `CanvasGestures`.
`Document` keeps ownership, the undo landing and applying; the write-back
is `TextWriteBack`, the caret's selection `CaretFollow`, and a pasted patch
file `PastedPatch`.
`ContractSurfaceTests` checks members as well as types, and what only the host
called is internal. The preset site's admin client is `Flyback.Site.Client`, which
`flyback-site` and `render-presets` both send through, and the site references
every plugin a page may link, Drawings included. `CompiledPatch` describes the
program; `Interpreter` runs it and `Arithmetic` holds the guards it and the IL
share. A tool's arguments are `ToolFields`, which its schema is rendered from and
its body reads through. A part with something to do at start
declares an `IStartAt` phase, and `Startup.Load` returns a `DesktopLaunch`.
Still written per shell, and small: the speaker handshake in two JS files, the
viewer's and the CLI's `--size` and `--oversample` option declarations, and the
viewer's `--cpu` against the CLI's `--processor`.
