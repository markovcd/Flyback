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

## 10. What has no test at all (Low to Medium)

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
- `shot` and `stills` are tested in Cli.Tests up to their refusals, `stills` also
  for the presets that draw nothing. What is left needs a seam: the hand-over's
  arguments to the editor (a fake editor), `stills` encoding (a fake ffmpeg) and
  its "no ffmpeg" refusal (an ffmpeg lookup that is not the process-wide PATH).
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

- `Plugins.Tests` is 89 files flat against seven `src` folders. Move only.
  Four files hold a fifth of Editor.Tests
  (`AssistantPanelTests.cs` 1,186, `SourceViewTests.cs` 1,154,
  `OutputSettingsTests.cs` 944, `NodeEditorTests.cs` 676) and
  `PatchWorkbenchTests.cs` is 1,420.
- Fourteen test files declare more than one top-level type:
  `PluginTrustTests.cs` (5), `PluginTrustSteps.cs` (3), `DecisionPluginTests.cs` (3),
  `FakeAssistant/RehearsedAssistantPlugin.cs` (3), and ten with two. Split as
  each is next touched.
- `ShippedPresetTests.cs:255` filters kinds with a `return` where a filtered
  `MemberData` would show the count.

## Order

Items 1, 2 and 3 are each one commit and go first: they are the ones that let a
real bug through or blame the wrong commit for it. Item 8 lands in the four
commits it names, each as its files are next touched. The rest as each file is
next touched.
