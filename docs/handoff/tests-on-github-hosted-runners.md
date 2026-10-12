# Every test on GitHub's runners

2026-10-12, against `7d46025`. On [TODO.md](../../TODO.md); delete this file in the commit that lands the last leg.

**Status:** the Windows and macOS legs run in Build on every push (`native-tests.yml`), and every push is on GitHub's runners (ADR-0195). The Android smoke passes on the Cloud gate's emulator. Left: the guard in step 4 of the order, and macOS's `Gpu` tests. `CglContext` opens only with `FLYBACK_CGL=1` (Cloud gate's `macos-gl` box), because on GitHub's Mac (Apple Software Renderer) it draws seven presets unlike the processor, by up to 255 of 255: Ahead and behind, Grid, Nebula, Ring scan, Whole band, Three channels and Feedback tunnel. Drawn as stills at 640×360, Ahead and behind (the Probe's saw edges moved, 8% of pixels) and Grid (worst 173) are far apart; Nebula, Ring scan, Three channels and Whole band differ only along edges (worst 12–35), and Feedback tunnel agrees, so its failure is in the frames it feeds back. The software renderer is only on a Mac with no GPU, so these wait on a real Mac showing the same.

## What is wanted

Every test in the solution runs on GitHub-hosted runners, so no test needs one of the maintainer's machines to run, and the tests that only one operating system can run do run somewhere.

## Where it stands

The gate (`docker build --target gate .`) runs the whole solution in a Linux image that carries every tool a test asks for: ffmpeg, Node, Chromium, Mesa's software EGL for the `Gpu` tests, a dummy-driver `jackd` for the `Jack` ones, and an unlocked keyring. It already passes on `ubuntu-latest` (pull requests run it there in about eleven minutes), and `runner.yml` falls back to it on `main` once `RUNNER_STATUS_TOKEN` is set (ADR-0195).

So the gate covers everything Linux can run. What no CI run reaches today, on any runner:

| Tests | Why they skip in the gate | Count |
|---|---|---|
| Registry, file associations | `SkipUnless(IsWindows)`: the registry is Windows' | 7 |
| WASAPI, the Windows backend, the second picture driver (Direct3D) | Windows only | 3 |
| DPAPI secret store, UNC share paths, case-insensitive names | Windows only | 3 |
| `Flyback.Plugins.WinIO.Tests` | WinIO opens nothing off Windows | the project |
| CoreAudio, CoreMIDI, the Keychain store | `SkipUnless(IsMacOS)` | about 5 |
| Android editor | `android.yml` builds the APK; `scripts/android-smoke.sh` is run by hand | the smoke |

The Windows self-hosted runner does not reach the Windows rows either: it runs the gate in a Linux container.

Left out on purpose: the `SHOT_DIR` tests draw the site's pictures and are tools, not checks; the donation-address test skips in any build that carries none.

## The plan

Three legs beside the gate, each its own job in `ci.yml` on GitHub's runners, on every push and pull request. None replaces the gate, which stays the one definition of what a change must pass (ADR-0120); the legs add the operating systems the image cannot be.

### 1. Windows: `windows-latest`, native

`dotnet test --solution Flyback.slnx -c Release` on the runner itself, with the same `--hangdump` and JUnit flags as the gate and `gate.sh`'s summary.

- ffmpeg: `choco install ffmpeg` (or a pinned zip, which fits the pipeline rules better than a moving package).
- Chromium: Chrome is preinstalled off PATH; set `FLYBACK_CHROMIUM` to it.
- Node: preinstalled.
- `Gpu`: no GPU on the runner. Direct3D falls to WARP, its software rasterizer, which should carry the second-driver test; OpenGL has nothing, so leave `Category=gpu` out with `--filter-not-trait` until a Mesa `opengl32.dll` is tried.
- `Jack`: leave out; the gate runs it.
- `FLYBACK_GATE` unset, so a spec whose tool is missing skips instead of failing.
- Risk: the rendered-picture snapshots may differ by font rasterization. A difference is a finding to fix in the test's inputs (a bundled font), never a wider tolerance (`honest-tests.md`).

About twenty minutes; a 60-minute timeout.

### 2. macOS: `macos-latest`, native

The same command, for `Flyback.Plugins.Tests` at least (MacIO and Keychain live there), the whole solution if it holds.

- Keychain: a hosted Mac's login keychain is locked. Create and unlock one for the run (`security create-keychain`, `security unlock-keychain`, `security default-keychain`), as the gate unlocks gnome-keyring.
- CoreAudio: the runner has no output device; the tests that open one must skip saying so, as the ALSA ones do.
- `Gpu` and `Jack`: leave out.

### 3. Android: `ubuntu-latest` with KVM

`android.yml` already builds the APK. GitHub's Linux runners expose KVM, so `scripts/android-smoke.sh` can run on an emulator after the build (`reactivecircus/android-emulator-runner`, pinned by SHA). Its own job, so a slow emulator never holds up the APK. It runs before a release, not on every push: `/release` refuses a commit it has not passed on, and on every push it would mostly add emulator flakes.

### 4. Linux: unchanged

The gate on `ubuntu-latest` when no self-hosted runner is online. Nothing to add.

## Order

1. Windows leg, `continue-on-error: true` until a run is green, then required. It reaches the most tests nothing runs.
2. macOS leg, the same way.
3. Android smoke.
4. A `ContractSurfaceTests`-style guard: a test that fails when a `SkipUnless(IsWindows|IsMacOS)` appears in a project no leg runs on that OS.

## Cost

Free: the repository is public, and standard hosted runners cost nothing for public repositories, macOS included. The legs run in parallel with the gate, so a push waits for the slowest, likely the Windows leg.
