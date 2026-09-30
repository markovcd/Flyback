# The sound steps its oversampling down when it cannot keep up

Planned on 2026-09-30, on `main` at `89f654cd`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

## Why

The sound path runs the patch at 4× the output rate (ADR-0023): 192k evaluations a
second. A machine that cannot hold that stutters. The web viewer notices (`judge()` in
`src/Flyback.Web/wwwroot/main.js` counts dropouts and hands the picture the clock), but
its only answer is to drop the sound. The desktop notices nothing: `AudioEngine` neither
times its callback nor counts a late one, so a slow laptop stutters with no word said.

Halving the oversampling halves the sound's cost, since the cost is linear in
evaluations. Stepping 4× → 2× → 1× on a machine that is falling behind trades a little
aliasing for a sound that plays, which is a better trade than silence or stutter.

The sample rate stays 48 kHz; only the factor moves.

## What is in the way

The evaluation rate is not only a quality setting. It is the rate the patch runs at, so
changing it changes more than aliasing.

**What a patch sounds like.** A module that measures its own step (ADR-0030, ADR-0041,
ADR-0042) is right at any rate. Anything that does a fixed amount per evaluation is not:

- A feedback wire carries the previous evaluation, so a loop with a fixed coefficient
  has a time constant counted in evaluations. `SlowWeatherPreset.Followed` keeps
  `Kept = 0.9999` a step: about 50 ms at 4×, about 200 ms at 1×. Read in the code, not
  measured.
- `Spectra`'s segment is counted in evaluations: 85 ms and a bin every 12 Hz at 4×,
  four times longer and finer at 1×. An Analyzer would draw differently.
- Buffers sized in seconds (`Delay`'s `Longest`, the Limiter's lookahead) follow the
  rate through `DelayState` and are fine; they are listed so the audit need not
  re-check them.

This is already live: the assistant listens at `WorkbenchLimits.ListenRate`, half the
speakers' rate (`PatchWorkbench.Senses.cs`), so it hears SlowWeather's follower twice as
slow as a person does.

**The switch resets the sound.** `DelayState.Adopt` cannot carry memory across a rate
change, so every delay tail, reverb and held level starts from nothing.

**A hand-built loop cannot be fixed by the catalog.** Shipped modules and presets can be
made rate-independent; a user's own `math.mul` feedback loop counts evaluations by
construction. After step 1 that is the one thing a step down still changes.

## The plan

Each step lands whole on its own.

1. **Audit and fix rate dependence.** Find every shipped module and preset that counts
   evaluations instead of seconds, and make it measure its step (the ADR-0041 way) or
   size itself in seconds. A test renders each at 4× and 1× and compares below the
   aliasing band. SlowWeather's follower and `Spectra` are the known two; the rest is
   the audit. Useful already: the assistant's half-rate listen becomes faithful.
2. **Let an agent pick the factor.** `flyback-viewer --oversample 1|2|4`, and the same
   on `flyback-cli`'s render where it applies, so the 1× sound can be heard and
   compared on a fast machine. `AudioRenderer` already takes the factor; nothing passes
   one.
3. **Measure the desktop.** Time each `Render` in `AudioEngine`'s callback against the
   buffer's length, allocation-free, and expose it as the sound's speed (seconds
   rendered per second spent). It feeds the `flyback-viewer --report` TODO item and the
   step down. The web viewer's live-speed TODO item is the same number for the page.
4. **Step down.** One policy, in both the desktop engine and the web viewer: when the
   speed holds near 1× (or dropouts pass a threshold), restart the sound one factor
   lower, 4× → 2× → 1×, and only after 1× fails give up on the sound as the page does
   today. One way only: never step back up in a run, or it cycles
   stutter → down → fine → up → stutter. The status line and the editor say the factor
   the sound is at.

## Decided

- **Exports stay at 4×.** An export is not real time; a slow machine takes longer and
  writes the same file. Only the live sound steps down.
- **The reset is accepted.** It happens once, on a sound that was already stuttering.
  In the page it costs nothing extra: `judge()` already stops and restarts the sound.
- **The factor is not a setting.** It follows the machine. The flag in step 2 is for
  checking, not for users to tune.

## Open

- The threshold: speed below some margin over 1× for some seconds, or a dropout count,
  or both. Step 3's numbers from a slow machine should decide it.
- Whether the step down needs an ADR amendment to 0023, which states 4× as the rate.
