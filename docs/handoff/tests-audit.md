# Where the tests are weakest, and what would fix each point

Written on 2026-10-09, on `main` at `85edbcc`. An audit, not a plan: each item is
a proposal, and goes to TODO.md only once the user picks it. Delete an item here
in the commit that lands it, and the file when the last goes.

Code lines are `grep -cvE '^\s*(//|$)'`. The suite is about 80,000 code lines in
22 projects under `tests/`: 1,643 test methods in Editor.Tests, 1,559 in
Core.Tests, 852 in Plugins.Tests, 462 scenarios in 121 feature files, 98 vitest
cases under `worker/`. The gate runs about 15,100 tests in under two minutes
(Editor.Tests 1m53s, Specs 40s, Plugins.Tests 20s, Core.Tests 14s), with 22
skipped. The counts and durations come from the Build workflow's logs; everything
else was confirmed by reading, and nothing was run here.

What holds and is not raised again: every test method is a sentence (no
`Should_`, no `Test` suffix, nothing lowercase); Shouldly is the only assertion
library and the only `Assert.` is a skip; doubles are private classes in the
file, as the guide says; every theory exception list names its members with a
reason (`GlslEmitterTests.WritesNothing`, `ContractSurfaceTests.Promised`); the
snapshots are exact and a mismatch names the pixel; every disposable test class
disposes; nothing writes under the repo or its bin; no feature file carries a
tag, and no step is undefined or ambiguous; the threat model's doors are mostly
tested with hostile input (zip-slip, size caps, a changed plugin, an unsigned
package, a list changed after signing, `--seconds` at infinity); every flake
found this week was fixed in the harness the same day, in the open.

## 8. The harness and the doubles are written many times (Medium to Low)

`Flyback.Specs` builds on `Flyback.Ui.Testing`: its `EditorApp` is
`HeadlessApp.Build` and its drivers settle through `UiTest.Settle`. What it still
writes for itself is a key `Press` and the turn-take, `Run` and close-on-dispose
trio, three times (`EditorDriver.cs`, `ViewerRun.cs`, `AboutSteps.cs:25-50`).

Counted across `tests/`, each a private copy the guide's shared place could
hold:

- `Loopback : IAudioDevice`, the double the guide names, eight times
  (`StatusCountTests.cs:92`, `ExportSteps.cs:154`, `StatusBarSteps.cs:68`,
  `ViewerWindowTests.cs:30`, `AudioEngineTests.cs:37`, `LineInTests.cs:24`, and
  two "records Disposed" variants). `Canned : HttpMessageHandler` four times
  across the provider test projects. MIDI stand-ins (`IMidiInput`, `IMidiPort`)
  four times each, bodies byte-identical. `FakeStore : ISecretStore`,
  `Transcript : ITranscript`, `Unreachable` and `Recorder` handlers twice
  each, byte-equivalent. `Collected : IUsageSink` re-declared beside the shared
  `CollectedEvents.cs`.
- `Named<T>` (find a control by `Name`) thirteen times in Editor.Tests,
  `Until` (a spin-wait with a deadline) seven times beside `UiTest.Pump`, three
  of them byte-identical and some returning `false` silently where `Pump`
  throws with the expression; press-a-named-button and close-a-dialog helpers
  about twenty times, 28 files raising `Button.ClickEvent` by hand against four
  calling `UiTest.Press`, and five `Close(window, dialog, by)` copies that find
  the button by caption, which the guide forbids.
- `PluginHost.Load(DefaultDirectory, PluginTrust.Shipped(...))` eleven times
  across nine Specs step classes (static, `Lazy`, `??=`, inline), so the specs
  load the shipped plugins up to nine times a run; Plugins.Tests has
  `ShippedPlugins.Loaded` once.
- `Run(Patch, float[])` (compile, evaluate a buffer, collect the output)
  md5-equal in `BellTests`, `DrumTests`, `FmTests`, `HissTests`, and the same
  loop in three more, while `Figures.cs:71` and `Fractals.cs` hold public
  versions nobody else calls.
- Four preset-site `HttpMessageHandler`s answering `/api/v1/presets`
  (`Editor.Tests/Ui/FakePresetSite.cs`, `SharedPresetSteps.cs:124`,
  `SiteSteps.cs:164`, `RenderPresetsTests.cs:372`); the first two serve the
  same gallery feature.

Fix, each its own commit: Specs' drivers build on `UiTest` (`Press`, `Pump`,
`Named`), with one `HeadlessWindow` base for the turn trio; Editor.Tests deletes its `Until`, `Named`, press and close
helpers for `UiTest` and `EditorTest` members, with the dialog closer finding
by `Name`; `LoopbackDevice` and `Canned` move into `Flyback.Plugins.Testing`,
whose remit widens from "what sound tests share" to "what plugin-facing tests
share"; one `FakeSecretStore`, `FakeTranscript`, `Unreachable`, `AuthorizationRecorder`
and MIDI pair per project; `Specs/Support/ShippedPlugins.cs`; one `Played` in
Plugins.Tests.

## 9. Tolerances and comparisons that are looser than their rule (Low)

- `Compile/JsProgramTests.cs:24` compares with `Hair = 1e-5f` on every op; the
  guide and ADR-0035 say bit for bit, and the comment's reason (`Math.sin`'s
  last bit) holds for the transcendental ops only. Bit for bit, with a named
  list of ops that get the hair.
- Without a reason beside them: `Graph/ReverbTests.cs:200` 25% relative;
  `Drawings.Tests/PathTests.cs:136` 15%; `MaximizerTests.cs:96` and
  `CrossoverTests.cs:56` ±0.5 dB where the same file uses 0.05;
  `EqTests.cs:75` 0.15 dB against 0.1 on its neighbors; `NoiseTests.cs:73,117`;
  `DelayTests.cs:71-72` 10% on a half-split impulse against 1e-3 around it;
  `AnalyzerTests.cs:144,158,175` 20% windows; `HarmonographTests.cs:86`
  `(0.3, 0.7)`; `SampleTests.cs:293`, `DodgePresetTests.cs:91`, `TextTests.cs:92`.
- `NoSenseDubPresetTests.cs:183` `(160 * Beat).ShouldBe(130, 0.5)` asserts
  arithmetic on the test's own constants; nothing under test is read.
- Weak-only assertions: `ClipFormatTests.cs:54` (every format "says what to do
  with sound": `ShouldNotBeEmpty` on a string), `PresetSiteDefaultsTests.cs:39`,
  `RecordKindsTests.cs:119`.

## 10. What has no test at all (Low to Medium)

- The web shells: `src/Flyback.Editor.Web` (656 code lines) and
  `src/Flyback.Viewer.Web` (761) are named by no test, and `wwwroot/*.js`
  (`main.js`, `speakers.js`, `gl.js`, `session.js`, `tap.js`, `microphone.js`)
  have no JavaScript tests; the only vitest is `worker/`. `hear.mjs` reaches
  the viewer's sound and nothing else. A vitest file for the pure parts of
  `session.js` and `tap.js`, run by the gate as the Worker's are, and one
  `hear.mjs` scenario that feeds a line-in buffer. Each shell's `main.js` runs
  only in a browser: the editor's starts Avalonia.Browser, which needs a DOM, so
  the page scenarios drive the desktop editor headless and `window.flyback` is
  reached by nothing, and the viewer's wake lock, sizes and landscape are text
  checks (`Site.Tests/ViewerScriptTests`). A headless Chromium in the gate image
  would reach both.
- Android: `Flyback.Editor.Android` and `Flyback.Plugins.AndroidIO` are
  `Build="false"` in the solution, referenced by no test, and `android.yml`
  builds an APK and tests nothing. The declaration theories
  (`ModuleDeclarationTests`, `AudioOutputSelectionTests`) read from the
  AndroidIO assembly without running it, as `PluginPackageTests` does for others.
- Settings sections: `SoundSection`, `MidiSection`, `PictureSection` (214),
  `RecordingSection` (217), `PrivacySection`, `KeyRows` are named by no test;
  the tabs are covered one fact at a time, so a field added without a fact goes
  unnoticed. A theory over every `ISettingsSection`: set each row to a
  non-default, save, reopen, equal.
- Shortcuts: keys are handled in twelve editor files and listed in
  `Inspect/InspectorHelp.cs` (43 rows) and on `site/index.html` (10 `<kbd>`),
  and no test ties the three. A `Specs` scenario that every `<kbd>` on the site
  is a row in the help.
- `shot` has no in-process test in Cli.Tests (`ShotCommand.cs`, 103); only the
  specs drive it. `stills` has them for the presets that draw nothing and for
  its folder; the encoding path needs an ffmpeg and a seam to fake one.
- Unreached after reading, largest first: `Canvas/CanvasPainter.cs` (509; the
  window tests settle it and nothing asserts a pixel outside `SHOT_DIR`),
  `ShellLayout.cs` (439), `Gallery/GalleryLayout.cs` (438), `Gallery/SiteRun.cs`
  and `GalleryChoice.cs` (240 each), `Ui/Controls/GpuPreviewSurface.cs` (296),
  `Flyback.Gpu` (1,243 across `Gl.cs`, `WglContext.cs`, `EglContext.cs`,
  `GpuReadback.cs`; `GpuRenderTests` only, which skips without Mesa),
  `Keyring/SecretTool.cs` (107; its output parsing has no fixture).
- `tools/TestOnlyMembers` is in the solution and run by nothing: not the gate,
  not a workflow. Its listing was committed by accident and removed on Oct 8
  (`6dbf6ec`) with ten rows in it, among them `MovieRenderer.Render`,
  `IlProgram.Compile` and `ImageLibrary.Count`; whether they still stand needs
  the tool run. A weekly job beside coverage, or a gate step against a
  committed baseline.

## 11. Shape and drift (Low)

- `Editor.Tests/Ui/` holds 120 of 172 files in a folder `src/Flyback.Editor`
  does not have; `Plugins.Tests` is 89 files flat against seven `src` folders.
  Move only. Four files hold a fifth of Editor.Tests
  (`AssistantPanelTests.cs` 1,186, `SourceViewTests.cs` 1,154,
  `OutputSettingsTests.cs` 944, `NodeEditorTests.cs` 676) and
  `PatchWorkbenchTests.cs` is 1,420.
- Fourteen test files declare more than one top-level type:
  `PluginTrustTests.cs` (5), `PluginTrustSteps.cs` (3), `DecisionPluginTests.cs` (3),
  `FakeAssistant/RehearsedAssistantPlugin.cs` (3), and ten with two. Split as
  each is next touched.
- `ShippedPresetTests.cs:255` filters kinds with a `return` where a filtered
  `MemberData` would show the count.
- The tests skill says "about 6900 tests in Flyback.Editor.Tests"; the last run
  here counted 7,622. The guide's project table lacks `Flyback.Plugins.Drawings.Tests`
  and `Flyback.Plugins.FakeDecider`.
- The gate runs no tests on a commit that changes nothing in the build context
  (the layer cache, as `ci.yml` says), so a green check on such a commit is
  the cache's, not a run's; the four docs-only reds in item 2 ran only because
  the red layer before them was never cached.

## Order

Items 1, 2 and 3 are each one commit and go first: they are the ones that let a
real bug through or blame the wrong commit for it. Item 8 lands in the four
commits it names, each as its files are next touched. The rest as each file is
next touched.
