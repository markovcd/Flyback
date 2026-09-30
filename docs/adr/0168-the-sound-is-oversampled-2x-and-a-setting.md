# ADR-0168: The sound is oversampled 2× by default, and the factor is a setting

**Status:** Accepted · 2026-09-30 · *user-directed* · amends
[0023](0023-oversample-the-audio-path.md) · implemented in
`src/Flyback.Engine/Render/AudioRenderer.cs`, `src/Flyback.Ui/OutputSettings.cs` and
`src/Flyback.Ui/Audio/AudioEngine.cs`

## Context

ADR-0023 evaluates the sound at 4× the output rate. A spike, written up in
`docs/handoff/sound-step-down.md` at `8cf32ce0`, measured what that buys. The cost is linear in the
factor: Whole band renders at 3.3× real time at 4× and 6.5× at 2× on the desktop, and a
browser has about 2× to spare at 4×. The 64-tap decimator is cleaner at 2× than at 4×
(flat to 18 kHz, images below −77 dB, where at 4× it is 3 dB down at 20 kHz and lets
26 kHz through at −25 dB). A naive saw aliases about 6 dB more at 2×. Listened to, the
differences between 2× and 4× across the shipped presets are barely audible.

## Decision

**2× is the default, and the factor is a setting: 1×, 2× or 4×** in Settings → Sound,
saved in `output.json`. It is a render setting like the others: the editor's sound and
its takes, `flyback-cli render` and `flyback-viewer` all follow it, and `--oversample`
overrides it for one run. The page has no row for it and plays at 2×.

**Presets are not made to sound the same at every factor.** A patch built on a loop
counted in evaluations, and Noise, whose level in the audible band rises about 3 dB for
each halving, change a little with the factor; that is accepted.

**Live sound that keeps falling behind is worked out a step lower**, 4× to 2× to 1×,
and never back up in a run: a quarter of two seconds' buffers late, and at least four,
is one step, and a new factor plays three seconds before it is judged. The engine times
each buffer of compiled sound against how long it plays for. A checkbox under the
factor, on by default, allows it. Not while a take is recorded: a take is written whole
whatever the speakers do, so it keeps the factor set, as a render does. The page's
worker times its chunks and judges them by the same `OversampleStepDown`, always on;
at 1× the rule says the sound is behind, which the web viewer answers by giving the
sound up and the desktop ignores. A new factor in the page is a new script, which runs
slow until the browser has warmed to it, so its chunks count only once it has played
three seconds. An edit's script is not held back: repeated edits to Acid at 4× came
with no late chunks, since a changed value leaves the script as it was.

**A change of factor starts what the patch remembers anew.** Delay lines and rings are
sized for the rate, so the engine swaps in a renderer at the new rate with the clock
where it was and the memory empty, in the same write as the program.

## Consequences

- The sound costs half of what it did everywhere, which is most of the headroom a
  heavy patch in a browser had been missing.
- ADR-0023's reasoning stands; only its number moves. Its aliasing test still compares
  1× against 4×.
