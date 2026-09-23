---
name: authoring-presets
description: Use when building, porting or measuring a shipped Flyback preset (showcase or small teaching preset) - how it is written and checked, what bounds one (audio cost per op, the Noise module's price, the canvas-fit test), and why not to cost-cut a preset that plays.
---

# Authoring and measuring presets

The user wants showcase presets that push the engine but still play live; they listen and watch in the GUI and report back.

## Cost

Measured on the user's machine: the audio path runs at 4x oversampling on one thread, and about 1,950 ops came to 0.66x real time. Op counts mislead: a Reverb is ~10% of real time, a 16-step sequencer ~3% (about six ops a step), a Filter ~1%. ADR-0096 sweeps ops nothing reads, so an unwired output costs nothing, `audio.noise`'s white is 1 noise lookup (pink is 13), and patches are 2-19% smaller than before it (Acid 1,360 -> 1,219; Bronze 1,848 -> 1,488; Outrun 2,149 -> 1,960). Bronze (1,850 ops, one Reverb) rendered at 0.62x; Outrun (2,150 ops, two Reverbs) plays live fine.

## Building and checking a big preset

- Write the preset directly in C#: a `PresetBench` subclass in `Flyback.Plugins.Effects`, like Bronze, Outrun and Phase. The user does not want an `.fbks` prototype as a step.
- To measure, add a throwaway xunit test that writes `PatchIO.ToJson(preset.Build(...))` to the scratchpad, then use `flyback-cli info/check/render` on the `.fbk`. The CLI's bin folder needs `Effects`, `Picture` and `Voice` copied into its `plugins/` from the test project's output. Delete the scratch test before committing.
- **Render with the Release CLI** (`dotnet build src/Flyback.Cli -c Release`, plugins copied from the Release test output). The Debug CLI is several times slower: a 170 s track had not finished after 11 minutes, while Release rendered it in about 2.5 minutes.
- CLI render timings are noisy (0.74x to 1.34x for the same patch while the app is running), so never cut features from a preset on the strength of one slow render. The user objected when that was tried on Outrun ("the patch runs fine"). Compare a new preset against a shipped one in the same run.
- Check levels from the WAV. Struck music sums to far over unity, so set the desk trim so peaks stay under the Clamp.
- `ShippedPresetTests` requires the patch to fit the 15000x10000 canvas with its groups off; 311 modules (Bronze) fits.
- A CPU render of a full-length clip is too slow; the user records video from the GUI. A 4 s low-res `.avi` is enough to check feedback trails.
- To tune a long song without rendering all of it (the CLI has no start offset), reorder the `State.notes` of the one-step-a-phrase lanes in the dumped `.fbk` with a few lines of Python so the phrases wanted come first, and render a minute.
- A Note snaps to the semitone, so a real glide is a Slew on the hertz after it. A Sequencer's gate dips to nought at every step edge, so read a flag from its value, not its gate.

## Small teaching presets

- Prototyping as `.fbks` through `flyback-cli check/render/print` is fine as a private step and much faster than a build (the CLI's plugins folder has Voice/Picture/Effects/Mastering), but short names clash once plugins load (`hsv`, `mix` need `color.hsv`, `math.mix`).
- An engine preset also needs its text form in `docs/language.md` section 14 and a `Same`/`Alike` case in `LanguageTests`, plus an approved PNG and two GLSL snapshots. Plugin presets need none of that.
- Level teaching presets to about -13 to -18 LUFS (`render --loudness`, 16 s). A Scan's `scale` only scales its drawn `view`, not `out`. `ScaleExtra.Set` takes absolute pitch classes (A minor pentatonic is 0 2 4 7 9).

## Formulas

`PresetBench.Formula("...", a, b, ...)` adds an Expression (ADR-0104). To move a preset's arithmetic onto formulas without changing a pixel, spell a constant the way C# folded it (`1 / 45`, `4 * (8 / 45)`, `45 * 2 * pi`; the formula folds number-on-number in float). Dump before and after, `render --at` a handful of stills and `cmp` them: Overworld's picture went 510 -> 381 modules with all eight stills byte-identical. Its sound moved the same way (381 -> 306), checked by comparing both compiled programs as sets of op fingerprints in a scratch test, then the full-length WAV.

Every preset is folded as it is built (`Presets.Fused`, ADR-0108, ADR-0109): no one- or two-input Maths module survives, and a test says so. A preset written with `Times`/`Plus`/`Product` arrives as Expressions anyway, and a number linked to a panel knob stays on a socket; hand-written formulas are only worth it where a formula reads better than the chain. To check a change to the folding, dump every preset from the previous commit in a scratch `git worktree` (it needs `dotnet restore` first) and compare `CompiledPatch.Ops` of each against the current build, register for register: all 47 matched.

Keep the website in step when a preset changes audibly or visibly (see the `site-audio-tracks` and `site-screenshots` skills). See also `played-presets` and `convenience-modules`.
