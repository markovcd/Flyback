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

## 1. The plugin presets never meet a compiled backend (High)

The IL, JS and GPU agreement tests, the GLSL snapshots and the frame snapshots
all run over `Presets.All` (`Compile/IlProgramTests.cs:26`,
`Compile/JsProgramTests.cs:31`, `Compile/GlslEmitterTests.cs:20`,
`Rendering/PresetSnapshotTests.cs:26`, `Cli.Tests/GpuRenderTests.cs:28`), which
is the engine's 26 presets. The 38 plugin presets (`MyceliumPreset.cs` 815 code
lines, `OverworldPreset.cs` 480, `BronzePreset.cs` 381, `WarehousePreset.cs`
377, `NoSenseDubPreset.cs` 362, and the rest) are compiled for the interpreter
only: `ShippedPresetTests.Every_preset_builds_and_compiles` and
`EveryModuleTests` stop at `CompileForAudio`. What touches plugin code on a
compiled backend is one `IlCompiler` in `EuclidKitPresetTests`, eleven
`GlslEmitter.Emit(...).PatchFragment.ShouldNotBeNullOrEmpty()` calls that prove
text was emitted, and the web viewer's three-row outline, skipped without Node.
`GpuRenderTests.Drawn` is a hand list of seven names. The IL test's own remark
says what this misses: a delay line, accumulator or cell numbered differently
shows up only on a shape that has one, and the plugin presets are the larger
shapes. The editor plays through IL, the GPU draws GLSL and the page plays JS,
so the presets a user hears are the ones the parity tests skip.

Fix: in Flyback.Plugins.Tests, theories over `ShippedPlugins.Loaded.Presets`:
IL equals the interpreter bit for bit over a second of sound and a grid of the
picture (the `IlProgramTests.ShouldMatch` shape), `GlslEmitter.Emit` succeeds in
both dialects, and the same for `JsProgram` where Node is present. `Drawn` runs
over all of `Presets.All`, or says in a comment why seven.

## 3. Tests mutate the process in assemblies that run tests in parallel (Medium)

Four UI assemblies pin `ParallelMode.Collections`
(`Editor.Tests/Ui/EditorTest.cs:26`, and the three `Headless.cs`). The other
ten take xunit 4's default, which runs every test in parallel regardless of
collection, and in those:

- `Cli.Tests/ProbeCommandTests.cs:338` sets `ONE_KEY`/`TWO_KEY`/`ALL_KEY` and
  `Dispose` nulls them; ten tests share the names, so one test's dispose unsets
  another's key mid-run. `CredentialsTests.cs:140` already does this right with
  a Guid suffix.
- `Cli.Tests/ViewerCommandTests.cs:21-34` sets `FLYBACK_TEST_ARGS` and the
  static seam `ViewerCommand.Beside`; its `[Collection("viewer")]` serializes
  only against other collections.
- `Plugins.Codex.Tests/CliTests.cs:186-196` and
  `Plugins.ClaudeCode.Tests/CliTests.cs:150-158` set the real `CODEX_API_KEY`,
  `OPENAI_API_KEY` and `ANTHROPIC_API_KEY`; `Plugins.Programs.Tests/LocatorTests.cs:56-63`
  rewrites `PATH`; `Specs/Steps/CodexSteps.cs:41-45` and `ClaudeCodeSteps.cs:41-45`
  prepend to `PATH` and restore a copy captured at construction, so two
  scenarios in flight restore in the wrong order while `WebViewerSteps.cs:470`
  and `NodeJs.cs:15` read `PATH` to find node.
- `Plugins.Tests/SecretStoreTests.cs:109-138` writes one account name,
  `flyback-test-account`, to the machine's real keyring from two tests; on a
  desktop with a locked keyring the first `Keep` can block on an unlock prompt.
- `Ui.Tests/StallTraceTests.cs:20-58` opens and closes a process-global static.

Build 599 hung in Plugins.Tests for thirty minutes and no commit names the
cause; this and item 4 are the candidates. Fix: the same `Parallelization`
assembly attribute in every test assembly, and per-test variable names in
`ProbeCommandTests`.

## 4. Waits with no cap, and assertions on the wall clock (Medium)

- `Viewer.Desktop.Tests/ViewerWindowTests.cs:89,173` `Player.Compiled().Wait()`
  with no timeout, on a compiler thread that runs below normal priority;
  `Specs/Support/Headless.cs:41` `HeadlessTurn.Gate.Wait()` with no cap, so a
  scenario that dies without `Leave` (`AssistantColumnSteps.cs:84` leaves only
  `if (shown is not null)`) deadlocks every later window scenario;
  `Core.Tests/Compile/NodeJs.cs:41` and `Specs/Steps/WebViewerSteps.cs:463`
  `WaitForExit()` on node with no timeout. Each ends only in the gate's
  ten-minute hang dump.
- `Plugins.Testing/JackDaemon.cs:119-131`: after sixty seconds on the lock the
  `IOException` propagates, so a sibling assembly holding it longer fails every
  JACK test from the fixture constructor instead of skipping with a `Why`.
- `Core.Tests/Graph/BeamTests.cs:200-203` asserts a noise draw is under five
  times a circle draw, best of three stopwatch runs: a ratio of two timings on
  a shared box. `Ui.Tests/StallTraceTests.cs:33` asserts a 5 ms sleep stays
  under a 100 ms stall threshold on a pool thread.
- `Editor.Tests/Ui/SitePresetTests.cs:235-288`: six plain `[Fact]`/`[Theory]`
  in an `EditorTest` class, which the guide and `UiTest.cs:17-22` say disposes
  from a pool thread and shows up as some other test failing later.
- Worst-case wait if everything hangs: about forty minutes in Editor.Tests
  (61 `Pump` calls at 30 s, `PresetListTests.cs:62` at 60 s four times), ten in
  Viewer.Desktop.Tests. Always paid: `PaletteByMeaningTests.cs:86`
  `Task.Delay(ModulePalette.Pause * 2)` with no comment, and 305 ms of sleeps
  in `StallTraceTests`.

Fix: `.Wait(TimeSpan, TestContext.Current.CancellationToken)` on the two
player waits, `Gate.Wait(TimeSpan)` that throws naming the holder,
`WaitForExit(ms)` then `Kill`, `TakeLock` expiry as a skip; `BeamTests` asserts
the operation count the drawer reports and `StallTraceTests` injects its
timestamps as its line 46 already does; `[AvaloniaFact]` on the six.

## 5. Tests that pass having checked nothing (Medium)

- Ten `if (...) return;` guards pass green with no assertion run and no skip
  shown. `Cli.Tests/CommandTests.cs:520` (render with a missing `--ffmpeg`
  fails saying so) returns whenever ffmpeg is on `PATH`, and the gate image has
  it, so the test has never asserted on CI. `Editor.Tests/Assist/ConversationStoreTests.cs:107`
  and `Plugins.Tests/PluginHostTests.cs:164` return off Windows;
  `Editor.Tests/Ui/PreviewBackendTests.cs:55` without a GPU;
  `Ui/AboutTests.cs:137` without an address. Six `SHOT_DIR` tests are tools
  that count as passing tests when the variable is unset (`Ui/PatchShotTests.cs:31`,
  `SkinShotTests.cs:113`, `PluginPreviewShotTests.cs:55`, `PluginArtShotTests.cs:47`,
  `QrCodeTests.cs:124`, `AssistantPanelTests.cs:186`).
- `Plugins.Tests/JackOutputTests.cs:30` `SkipWhen(server.Available)`: a test
  that runs only where the feature is absent, so on CI and every box with JACK
  it never runs.
- The secret-service round trip runs on no automated machine:
  `SecretStoreTests.cs:103-144` skip when `Here is null`, and the gate image
  has no keyring daemon.
- `PackageSigner.Checked` is false in every Debug build
  (`src/Flyback.Plugins/Hosting/PackageSigner.cs:20-32`), so a local
  `dotnet test` exercises the unsigned path only where a test passes
  `checkKeys: true`; `Site.Tests/CheckSubmissionTests.cs:187` skips in Debug.
- Written files asserted by `Length > 0`: `FfmpegClipWriterTests.cs:158,184,199`,
  `LiveRecorderTests.cs:122,149,348`. A one-byte file passes.
- `Specs` skips are silent in CI: five `TestIgnore`/`SkipUnless` sites
  (`ExportSteps.cs:78` GPU, `WebViewerSteps.cs:447` Node, `SoundFileSteps.cs:41`
  and `CliSteps.cs:373` ffmpeg, `JackSteps.cs:26`), and the gate image has all
  four tools, so a skip there means a tool regressed, and it reports as skipped.
- `Core.Tests/Graph/PatchIoTests.cs:273-276` `catch (JsonException) { return; }`
  lets "refused or opens" pass on a parser crash; `security.md` says parsers of
  untrusted text report, never throw.
- `Plugins.Tests/AlsaCaptureTests.cs:71-73` turns any
  `InvalidOperationException` into a skip, not only "no card".

Fix: `Assert.SkipUnless` for every guard, so the skipped count is honest;
`CommandTests:520` runs with an empty `PATH`; `JackOutputTests:30` tests `Form`
against a stand-in probe; a keyring daemon (`gnome-keyring`,
`dbus-run-session`) in the gate image; read the written files back through
`SoundReader` as line 220 already does; `TestIgnore` throws when `gate.sh` sets
a variable saying it is the gate.

## 6. The specs do not hold the rule they state (Medium)

The rule is that every feature a user patches, plays or does has a scenario,
and nothing checks it:

- 64 of about 102 catalog modules are named by no feature: 29 in Core (HSV, RGB,
  Blend, Ink, Vignette, Blur, Transform, Translate, Warp, Threshold, Tile,
  Checker, Note Sequencer, the arithmetic ones), all four Effects, all three
  Figures, six of seven Mastering, twelve of sixteen Picture, six of twelve
  Voice. Seven CLI commands (`probe`, `viewer`, `pack-plugin`, `plugin deny`,
  `plugin list`, `plugin key`, `plugin describe`). The Privacy settings section,
  and the fields `followTransport`, `fullScreenOn`, `jpegQuality`, `latency`,
  `previewFrameRate`, `rewindBeforeTake`, `takeover`, `updates`. Of the 43
  shortcuts in `Inspect/InspectorHelp.cs`, Ctrl+F, Ctrl+Click, Ctrl+A, Ctrl+E,
  Ctrl+L, wheel zoom, Esc on a drag and Ctrl+O. Thirty plugin presets are
  covered only by the "every shipped preset" scenarios.
- The website scenarios assert on JavaScript source text:
  `Steps/WebsiteSteps.cs:84-150` `script.ShouldContain("navigator.wakeLock.request('screen')")`,
  `ShouldMatch("""if \(!lacks\) \{\s*buttons\.appendChild...""")`. The scenario
  reads as a requirement; the test passes when those characters are in that
  order. This is the test that broke in item 2 when `main.js` was rewritten
  with the behavior intact.
- The web editor has no browser and no Node: `Steps/PageSteps.cs:11-29` runs the
  desktop editor headless with a dictionary for the browser store, so the 23
  page scenarios never touch `wwwroot` or a JS engine. The web viewer, by
  contrast, runs the real wasm build under `node hear.mjs`
  (`WebViewerSteps.cs:443-460`).
- About twelve features carry a scenario written as mechanics: a text-language
  docstring used as wiring (`PatchLength.feature:27,37` `t.progress |> out.left`,
  `Oversampling.feature:7`, `RenderingFromASecond.feature:6`,
  `LineIn.feature:18,49`, `CommandLine.feature:21`), sample arithmetic
  (`Cycles.feature:16` "0.25, 0.375, 0.4375, 0.46875", `Continuity.feature:21`),
  backend words (`WebEditorPreview.feature`, `RendererNames.feature:13-16` raw
  GL strings), and `RecompilePacing.feature`, an engine-pacing change the tests
  skill says gets no scenario, with its C# twin in `RecompilePacingTests`.
- `Steps/DecisionSteps.cs:73,164,192` call `DecideCommand.Run` and
  `CheckCommand.Run` directly, so "flyback-cli decides" never sees the parser or
  `--json`; every other CLI step goes through `InProcessCli.Run`.
- Two steps no feature uses: `PromptStartSteps.cs:84` and `VolumeSteps.cs:22`
  (the toolbar's Volume clicked all the way up: a requirement nobody stated).

Fix: theories in Plugins.Tests over `Modules.All`, `Presets.All` and the CLI's
command tree, each asserting the name appears in some feature, with a named
exception list, so the next module without a scenario fails the gate. The
website steps run the page scripts under Node with a DOM stub, as `hear.mjs`
does, or move to Site.Tests as the text checks they are. The page gets a
`hear.mjs`-style entry for `PageScript` and `PageSound`. Each docstring becomes
a phrase and the number moves into the step; `RecompilePacing.feature` goes.
`DecisionSteps` route through `InProcessCli.Run`.

## 7. Saved data and the threat model, where the test is missing (Medium)

- No committed fixture of a patch layout: `PatchIO.FormatVersion = 1`,
  `Upgrade` has no step, and `find tests -name '*.fbk*'` finds nothing.
  Allowed before 1.0.0, but the first raise has nowhere to put its fixture.
  `tests/Flyback.Core.Tests/Graph/layouts/v1.fbk` and `.fbkb`, and a theory
  over that folder, `Every_layout_ever_written_still_opens`.
- Socket order is pinned by nothing. The snapshots pin compiled output for 26
  engine presets; no test lists every module's sockets by position. One
  committed text file of `PluginHost.Load().Modules.All` as
  `typeId | inputs… | outputs…`, compared with Shouldly in Plugins.Tests, so an
  insertion anywhere but the end changes a verified file.
- `set_sample` and `set_picture` (`src/Flyback.Plugins/Assist/ModuleEdits.cs:253-284`)
  store any string; `PatchWorkbenchTests` passes only `drums.wav` and `moon.png`.
  The check is downstream (`PatchPathsTests`), and nothing shows the two meet:
  a workbench test with `\\host\share\x.wav`, `../../x.wav`, `/etc/passwd`.
- `KeyedTransport.cs:14` sets `AllowAutoRedirect = false`; `grep -ri redirect tests`
  is empty. A `Canned` 302 to another origin, one request made.
- `PatchIO.cs:34` reads with the default `MaxDepth`; the length caps are tested
  (`A_chunk_that_lies_about_its_length_costs_nothing`,
  `A_bundle_that_unpacks_past_its_limit_is_refused`) and depth is not.
- A 200 whose body is HTML or truncated, in the OpenAi and Gemini
  `SessionTests`: refusals, rate limits and non-JSON tool parameters are
  covered, this is not.
- `pipeline.md`'s rules for workflows (every action SHA-pinned, `permissions:`,
  `timeout-minutes` on every job) are read by no test. A fact over
  `.github/workflows/*.yml`.
- `PageSettings` has only the kept-nothing case; a garbage store opening as the
  defaults.

## 8. The harness and the doubles are written many times (Medium to Low)

The architecture audit's item 10 is half landed: `Flyback.Ui.Testing` exists
and the four UI assemblies build on `UiTest`. `Flyback.Specs` still does not
reference it (`Flyback.Specs.csproj:21-29`), and carries the third headless
`Application` (`Support/Headless.cs:54-66`, identical to `Ui.Testing/HeadlessApp.cs:14-18`
plus `Editor.Tests/Ui/TestApp.cs`), `EditorDriver.cs` (907 code lines) and
`ViewerRun.cs` with their own `Settle` (`EditorDriver.cs:1396`, `ViewerRun.cs:166`,
both one layout pass short of `UiTest.cs:83`), their own key `Press`, and the
turn-take, `Run` and close-on-dispose trio written three times
(`EditorDriver.cs:1297-1418`, `ViewerRun.cs:130-164`, `AboutSteps.cs:25-50`).
`Headless.cs` holds three top-level types.

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

Fix, each its own commit: Specs references Ui.Testing and its drivers build on
`UiTest` (`Settle`, `Press`, `Pump`, `Named`), with one `HeadlessWindow` base
for the turn trio; Editor.Tests deletes its `Until`, `Named`, press and close
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
  `hear.mjs` scenario that feeds a line-in buffer.
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
  and no test ties the three. `--help` is not checked to list every command
  `Program.Run` adds. A `Specs` scenario that every `<kbd>` on the site is a
  row in the help, and a fact that `--help` names all nineteen commands.
- `shot` and `stills` have no in-process test in Cli.Tests
  (`ShotCommand.cs` 103, `StillsCommand.cs` 123); only the specs drive them.
- Unreached after reading, largest first: `Canvas/CanvasPainter.cs` (509; the
  window tests settle it and nothing asserts a pixel outside `SHOT_DIR`),
  `ShellLayout.cs` (439), `Gallery/GalleryLayout.cs` (438), `Gallery/SiteRun.cs`
  and `GalleryChoice.cs` (240 each), `Ui/Controls/GpuPreviewSurface.cs` (296),
  `Flyback.Gpu` (1,243 across `Gl.cs`, `WglContext.cs`, `EglContext.cs`,
  `GpuReadback.cs`; `GpuRenderTests` only, which skips without Mesa),
  `Keyring/SecretTool.cs` (107; its output parsing has no fixture),
  `osc.triangle` (the one built-in id no test names).
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
- Fifteen test files declare more than one top-level type:
  `PluginTrustTests.cs` (5), `Specs/Support/Headless.cs` (3),
  `PluginTrustSteps.cs` (3), `DecisionPluginTests.cs` (3),
  `FakeAssistant/RehearsedAssistantPlugin.cs` (3), and ten with two. Split as
  each is next touched.
- `ShippedPresetTests.cs:255` filters kinds with a `return` where a filtered
  `MemberData` would show the count.
- The tests skill says "about 6900 tests in Flyback.Editor.Tests"; the last run
  here counted 7,622. The guide's project table lacks `Flyback.Plugins.Drawings.Tests`
  and `Flyback.Plugins.FakeDecider`.
- The gate runs no tests on a commit that changes only docs or a workflow (the
  layer cache, as `ci.yml:51-55` says), so a green check on such a commit is
  the cache's, not a run's; the four docs-only reds in item 2 ran only because
  the red layer before them was never cached.

## Order

Items 1, 2 and 3 are each one commit and go first: they are the ones that let a
real bug through or blame the wrong commit for it. Item 5's guards and item 4's
caps are an afternoon each. Item 6's "every feature has a scenario" theories
land once, and the modules they list are then scenario work over time. Item 8
lands in the four commits it names, each as its files are next touched. The
rest as each file is next touched.
