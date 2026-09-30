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

## Spike, 2026-09-30: is 4× needed at all?

Measured on `main` at `c7f0c9d6` with `flyback-cli render --oversample 1|2|4` (on the
`claude/sound-step-down` branch, not landed), every shipped preset rendered for 8 s at
each rate, and test tones analyzed in numpy. Not listened to by a person.

**Cost is linear in the rate.** 60 s of sound on this machine (i5-14600KF), start-up
subtracted:

| Preset | 4× | 2× | 1× |
|---|---|---|---|
| Whole band | 3.3× real time | 6.5× | 13.1× |
| Acid | 3.2× | 6.2× | 12.8× |
| Beat you can see | 25× | 44× | 102× |

In a page, where Whole band has about 2.0× at 4×, 2× would give it about 4×.

**The decimator is better at 2× than at 4×.** Its 64 taps are fixed, so at 4× the
transition band is twice as wide in hertz:

| | 2× | 4× |
|---|---|---|
| Response at 18 kHz | −0.02 dB | −0.9 dB |
| Response at 20 kHz | −1.2 dB | −3.0 dB |
| 26 kHz image, folds to 22 kHz | −78 dB | −25 dB |
| 28 kHz image, folds to 20 kHz | −77 dB | −43 dB |

At 4× the top octave is dulled and 24–30 kHz leaks back down; at 2× neither happens.

**Aliasing from inside the patch rises as the theory says.** Inharmonic energy below
20 kHz, in dB under the signal:

| Signal | 4× | 2× | 1× |
|---|---|---|---|
| saw 110 Hz | −39 | −33 | −27 |
| saw 1,234 Hz | −29 | −23 | −16 |
| saw 3,322 Hz | −24 | −18 | −12 |
| square 1,234 Hz | −31 | −24 | −18 |
| hard clip, sine 1,234 Hz | −61 | −48 | −36 |
| Drive 8, sine 1,234 Hz | −76 | −58 | −38 |
| Drive 8, sine 3,322 Hz | −49 | −38 | −24 |

A naive saw or square loses about 6 dB per halving and is not clean even at 4×: a high
saw sits at −24 dB. Saturation loses 10–18 dB per halving but stays at −48 to −58 dB at
mid pitches, under the note itself.

**What the presets do at 2×.** Overall level moves under 0.3 dB for all but Phase
(+1.7 dB), Loop (−0.7) and Echo chamber (−0.6). By band:

- Most presets, pure tones and filtered voices, move under 1 dB in every band.
- Noise is louder: Acid, Beat you can see, Euclid kit, Struck, Warehouse, First beat,
  No Sense Dub, Vigil and Whole band gain about 3 dB where their hats and hiss are
  (6 dB at 1×). Noise is made once an evaluation, so its power spreads over the whole
  internal band and less of it is filtered away at a lower rate.
- Hand-built loops change color: Loop loses 1–5.6 dB above 1 kHz, Echo chamber moves
  0.6 dB in one band, Mycelium gains 5 dB below 125 Hz, Phase about 2.8 dB across it.

**What 2× would cost, then:** 6 dB more aliasing on raw saws and squares, audible on
an exposed bright lead and masked in a mix; hats and hiss 3 dB louder unless Noise is
scaled by the rate; a few presets built on per-evaluation loops sounding different.
**What it would buy:** half the sound's cost everywhere, and a cleaner top octave.

**Recommendation.** One rate everywhere, so what is heard is what is exported: 2×,
with Noise's level scaled so it holds (`sqrt(4 / oversample)`), and the few presets
that change checked by ear rather than made rate-independent. 4× stays available as
`render --oversample 4` for a cleaner export when someone asks for it. The step down
in the plan above then starts at 2× and has one step, to 1×, which is the one that
changes presets audibly. This replaces ADR-0023's 4× and needs an ADR of its own.
