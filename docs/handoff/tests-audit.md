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
  `Build="false"` in the solution and `android.yml` only compiles them. The logic
  they hold is shared and tested on the desktop (`BlockWriter`, `BlockReader`,
  `MidiStream`, `EditorReadout`); the glue onto `android.*` runs only on a device.
  `scripts/android-smoke.sh` checks it by hand on the local AVD: the app boots on a
  preset, the picture draws, AudioTrack plays, the IL runs, the state broadcast
  answers, and all of it after a rotation. It moves into `android.yml` on an
  emulator when the APK ships, or the first time an Android-only break gets past
  the compile; until then its flakes would block `/release` on a build nobody
  installs. Reading the AndroidIO assembly in the declaration theories is not
  worth doing: it declares no modules.
- `shot` and `stills` are tested in Cli.Tests up to their refusals, `stills` also
  for the presets that draw nothing and, with an ffmpeg on the machine, for the
  WebP a preset that draws gets. What is left needs a seam: the hand-over's
  arguments to the editor (a fake editor), and the "no ffmpeg" refusal, which
  `Ffmpeg.Resolve` answers from the process-wide PATH.
- Unreached after reading, largest first: `Canvas/CanvasPainter.cs` (509; the
  window tests settle it and nothing asserts a pixel outside `SHOT_DIR`),
  `ShellLayout.cs` (439), `Gallery/GalleryLayout.cs` (438), `Gallery/SiteRun.cs`
  and `GalleryChoice.cs` (240 each), `Ui/Controls/GpuPreviewSurface.cs` (296),
  `Flyback.Gpu` (1,243 across `Gl.cs`, `WglContext.cs`, `EglContext.cs`,
  `GpuReadback.cs`; `GpuRenderTests` only, which skips without Mesa),
  `Keyring/SecretTool.cs` (107; its output parsing has no fixture, and would
  need a seam for the process it runs).

## Order

Each as its file is next touched.
