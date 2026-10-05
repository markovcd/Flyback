# Grab the output: a patch that knows which knob moves what

Planned on 2026-10-01, on `main` at `9d667a68`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked; spiked 2026-10-05 (below)

## What is wanted

Touch the picture or the spectrogram and the knobs move to make it so. Drag a ring's edge out
and its radius turns; brush an area warmer and the color knobs drift; pull a 2 kHz band down
and the filter closes. Because one knob can feed both the picture and the sound, dragging one
moves the other too. No other synth can offer that, since none compiles both from one
instruction stream.

## How it works

Every pixel, and every point of the sound, is a chain of ops from `x`, `y`, `t` and the knob
values. Each op has a known slope (`sin` has `cos`, a product has the other factor), so the
chain rule gives, for any output, how much it moves per unit of every knob at once, exact and at
about twice the cost of computing it. That is forward-mode differentiation: each value carries
its derivatives alongside it (dual numbers).

A gesture becomes a goal over a region (its pixels averaged into one number), and the solver
picks the smallest knob movement that reaches it while holding any area marked *keep*. Several
knob mixes usually fit; the smallest is the one taken.

Only knobs and constants move. A gesture never adds a module or a wire; that stays the
assistant's job.

## What a person touches

One pixel is imperceptible, so nothing works on one. In shipping order:

1. **Hover to see what owns it.** Point at the picture and the knobs that control that area
   glow by how much they do. Select a knob and the picture becomes a heat map of where it
   matters. Read-only, cheapest, useful alone.
2. **Grab a shape and drag it.** The goal is "the pattern under the cursor at the start is now
   under the cursor": a ring's radius, a blob's offset, a stripe's phase. The move that sells it.
3. **Brush a quality.** *Warmer*, *brighter*, *busier*, *smoother*, *slower*, and *keep*. Each
   stroke nudges; holding drifts.
4. **Sliders that are directions.** *Contrast*, *Detail*, *Motion*, *Warmth*, each a direction
   through every real knob, worked out afresh from the patch; the real knobs follow as it turns.
5. **Match a reference.** Drop a picture or a palette and the knobs settle toward it.

The sound gets the same five on the spectrogram: hover to see owners, drag a band, *brighter*,
*punchier*, *wider*.

The picture moves, so the clock pauses while a gesture is held. A goal held over a window of
frames is the later refinement.

## The command first

Drivability comes first, so the numbers exist as a command before any gesture:

- **`flyback-cli grad <patch> --at x,y[,t] [--json]`**: every knob's slope for that point's r,
  g and b. `--band <hz> --at-seconds <t>` does the same for the sound.
- **`flyback-cli fit <patch> [--image ref.png] [--sound ref.wav] [--steps N]`**: gradient
  descent on the knobs toward a reference, writing the patch it reached.

`grad` gives the assistant the answer it now guesses at: "turn `cutoff` down 0.3 and the
2 kHz bulge drops 4 dB."

It pairs with [measure.md](measure.md): measure pins each socket's value, `grad` adds how
sensitive the output is to it.

## The hard parts

- **Backends.** The rule for each opcode's derivative is written once, in the interpreter. The
  IL, GLSL and JavaScript backends then agree with it because the tests say so (ADR-0035). Ship
  on the processor; the GPU path waits until the processor's has been shown to pay.
- **Memory and feedback.** Previous-frame sampling and feedback carry derivatives through time.
  Truncate after N frames, as recurrent networks are trained.
- **Flat spots.** A step, a wrap, a `floor` or Dice has zero or no slope, so dragging there does
  nothing. Use a smoothed stand-in for the slope while fitting only, never while playing.
- **Locality.** A slope says what happens for small moves. A drag is many small steps; a leap
  ("make it a spiral") has no knob path and will not arrive.

## Sequence

1. Dual numbers in the interpreter, one derivative rule per opcode, tested against finite
   differences on every preset.
2. `flyback-cli grad`, with `--json`.
3. Hover in the editor.
4. Drag with *keep*.
5. Brushes and direction sliders, then `fit` and the reference drop.
6. An ADR at step 1, recording the derivative rule per opcode and the smoothing of flat spots.

## Spike, 2026-10-05: do slopes through the ops hold up on real presets?

Measured on `main` at `261b0bc9`, with code on the `claude/grab-the-output-spike` branch at
`fad95ef2` (local, not landed). Knobs compile as live inputs (`CompileForSlopes`). After the
interpreter runs, a second walk over the same ops carries one lane per knob, and the sound's
memory carries its own lanes. `flyback-cli spike-slopes` runs all 60 shipped presets,
plugins included. The picture is a 64×36 grid run from 0 to the frame at 2 s, with slopes
taken against the frame before held still. The sound is the left channel at 96 kHz, octave
levels over 8,192 samples ending at 1 s, for 8 knobs per preset. `flyback-cli grad` is
there too. Nothing was looked at or listened to by a person.

**The rules are right.** On the picture, every knob at 48 points per preset (about 326,000
checks) matches a central difference to 0.1% in 99.3% of them, and none is wrong. The rest
are of three kinds:

- 1,126 are knobs resting on a kink, such as a clamp's edge or a hue sector's boundary. The
  slope is one side's and the difference averages both sides, so they are off by exactly 2×.
- Some are points within a difference's step of an edge.
- 3 are a 0.4 Hz LFO driving FM hard enough that the difference's step moves a thin line off
  the pixel.

On the sound, through delay lines, allpasses, accumulators and loops, 80% agree. Another 18%
are bands where two finite-difference step sizes disagree with each other. The 45 that
disagree are all kinks at rest: a knob at its clamp's end, or a delay time on a whole number
of samples, which is the corner of the line's interpolation. Two echoes' `Echo.left` was
compared sample by sample to confirm the second kind.

**Exact slopes miss hard edges.** The region check compares each of 48 blocks' mean light
against a turn of 2% of each knob's travel. Of the 16,155 block-knob pairs that move
smoothly, exact slopes see nothing in 16.3%. On showcase presets it is far worse: Acid 80%,
Fracture 67%, Outrun 54%, and Spectrum 100%, whose bars are drawn by Step. These are the
plan's flat spots. A shape drawn by Step, Floor or Fract, or by a smoothstep narrower than a
pixel, has zero slope at every pixel center.

**A pixel's footprint fixes most of it.** Two more lanes carry the slopes against x and y,
which say how wide one pixel is in an op's own input. At Step, Floor, Ceil, Fract, Sign, Mod,
Abs, Clamp, Smoothstep and a picture read, the knob lanes take the op's secant across that
width instead of its derivative. This cuts blindness from 16.3% to 3.5% (Spectrum 1.9%,
Acid 6.2%, Outrun 1.7%), and the share of slopes within 2× of the actual move rises from
77.3% to 84.2%.

It hurts deep iterated chains. Julia walk's within-2× share falls from 93.5% to 27.9%, and
Dive's from 58% to 4%. The footprint grows through every iteration until the secant averages
the slope away. Fracture (37%) and Overworld (26%) stay partly blind. The rule needs scoping,
to small footprints or to the last edges before the sink. Which one is the first experiment
of step 1.

**Moves are short.** Of all block-knob pairs that move under a 2% turn, 46% cross something
on the way: the two halves of the move disagree in sign or by 3×. An unranged socket travels
-4 to 4, so 2% of it is 0.16. That is enough to push Trails' smoothstep `edge0` past `edge1`,
or Captions' `softness` of 0.006 below zero. A drag is many steps of well under 1% of travel,
and the travel of an unranged socket is no scale to measure them in.

**A drag reaches its goal.** The solver is min-norm Gauss-Newton on one block's light, +0.1,
with knobs measured in their travel. Each step is capped at 2% of travel and halved until the
error is no worse. It reaches the goal on 49 of the 54 presets that have a block to move, or
47 with footprint slopes. Holding a far corner's four blocks within 0.02 as well, it reaches
19; a dozen one-knob presets cannot, as their one knob moves everything. Two things were
needed:

- Each knob is weighted down by how much it moves the whole frame. Unweighted, the solver
  leans on a clock rate, whose slope is 10⁶ per travel and whose move falls below a float
  knob's resolution.
- A step that leaves the error equal is taken. A hard edge between pixel centers holds the
  light flat until it crosses one.

Phase misses every time: its block is a sub-pixel stroke moved by `Stroke.rate`.

**What it costs.** Per pixel, the interpreter and then the slope walk, as a mean over the
presets:

| | Time | Against plain |
|---|---|---|
| Plain | 0.60 µs | 1× |
| One knob | 3.98 µs | 6.6× |
| Every knob | 25.3 µs | 42× |
| Every knob, Overworld (216 knobs) | 221 µs | 91× |

A heat map for one knob at 160×90 is 57 ms. The owners of a 32×32 area take 26 ms, and 226 ms
on Overworld. The one-knob cost is the walk's own overhead: a span, a clear and three
multiply-adds per op. A walk fused with the interpreter should come near the plan's 2×.

On the sound, 8 lanes cost 7.5× a plain render. `grad --band` over all 213 of Acid's knobs
takes 50 s for one second of sound, which is usable from a script but not under a finger.

**What `grad` showed.**

- It takes `--point x,y --at <seconds>` rather than `--at x,y,t`, because `--at` is a second
  everywhere else.
- Ranking knobs by their whole travel flatters wide ones, such as a Sine's freq over
  0–20,000 Hz. A knob marked in decades or in Hz needs its own taper.
- Two modules with one title print one label. It needs `measure`'s numbered handles.
- A point is the wrong unit for hover. Beside every edge, the exact slope at a pixel center
  is zero, so hover has to read a region with footprint slopes.

**What changes in the plan.**

- Step 1's derivative rule per opcode is its local partials. They are read off the values
  the interpreter already left in the SSA registers, so nothing an op computes is written
  twice, and the same table drives forward and reverse mode. Each stateful op carries its
  memory's slopes beside the memory.
- Flat spots are not a detail of fitting. Hover and drag are blind without the footprint, so
  the x and y lanes and the secant rule belong in step 1, scoped so that iterated chains keep
  their slopes.
- At a kink, take the side the gesture moves toward, never whichever side the rule happens
  to pick.
- A drag weights knobs by their effect on the whole frame, keeps steps well under 1% of
  travel, accepts a step that leaves the error flat, and stops the clock while it runs. How
  far holding the frame before still is from the slope through the whole history was not
  measured.
- Reverse mode comes with drag (step 4), where one goal meets hundreds of knobs, and for
  sound slopes over many knobs, which then needs a tape through time.

## Trial, 2026-10-05: one gesture on Machine Room

Run on the preset site's default `Machine Room.fbk`: 304 modules and 24 panel knobs that drive
both halves. The tool was `flyback-cli spike-grab`, on the spike branch at `015a1cb1`.

The gesture asked for the middle of the frame to be 0.03 brighter at 30 s and 60 s, while
holding the 63 Hz, 125 Hz, 2 kHz and 8 kHz octaves within 0.75 dB. Only the panel knobs
could move, and `Tempo` was left out.

The answer was one knob: `Echo` from 0.25 to 0.55. That takes the trails' persistence from
0.80 to 0.86, and the echo's feedback from 0.33 to 0.54.

It was checked on 62 s clips from `flyback-cli render`, measuring 2 s around each moment. The
light went from 0.232 to 0.261 at both moments, and every octave stayed within ±0.4 dB. The
modified file was not committed.

**A quantizer at the end blinds every knob.** Exact slopes saw only the vignette. `posterise`
floors the whole picture and the bitcrusher floors the whole mix, so every knob upstream had
zero slope. The footprint can't help where a floor's input is flat across the frame.

A dithered floor gave every panel knob a slope on both sides. Here a floor moves one for one
with its input, as it does on average for an input that could sit anywhere between two
steps. It belongs in step 1 beside the footprint rule.

**The slopes steer and a real run judges.** Slopes taken against the frame before held still
were 1.5–2× short of a 30-frame move through the trails and slews. `Crush` and `Resonance`
had the wrong sign. Each step went where the slopes pointed, and was kept only if 30 real
frames and a real render of the octaves agreed it helped.

**Min-norm spreads a move thin.** The first answer turned 14 knobs a little. In the real
render, at 60 s, it took 2–4 dB off everything from 1 kHz up. The solver missed this because
it judged 0.17 s windows that started with a second of memory.

A second pass with only the knobs that carried the first answer gave the one-knob result. A
person wants the fewest knobs, so drag needs a sparse step: an L1 term, or this prune and
solve again. Sound goals need windows as long as a person listens, here 0.68 s with 2 s of
memory behind them.

**Holding the octaves is not holding the sound.** The longer echo tails are plain to hear in
a spectrum that did not move. A sound keep needs a measure of time as well as of level, such
as decay or onset.

**The picture needs the sound running.** Meters read the speakers' traces, and the spike's
picture never filled them, so it solved on a cyan tunnel where the real one is amber. The
light gesture transferred anyway, but a hue gesture would not have. Step 3 runs both halves
together, as the editor does.

**A still is not a check.** `render --at` draws one frame with no history, so the trails,
planes and meters are missing. The check had to render clips and measure frames pulled from
them.

## Another way: the same picture, a different sound

A goal on the picture pins a number or two, and a patch has dozens of knobs, so many knob
mixes reach the same picture. Where a knob feeds both halves, each of those mixes sounds
different. Offering them one after another turns the sound's unpredictability into something
a person can steer: the picture stays as asked, and they browse the tracks behind it.

**Two ways on Machine Room.** The same gesture was run twice: the middle of the frame 0.03
brighter at 30 s and 60 s, judged on real renders. Both reached it on the picture.

| Way | Knobs | What the sound did |
|---|---|---|
| Sound held | `Echo` 0.25 → 0.55 | Every octave within ±0.4 dB; the echoes ring longer |
| Sound free | `Crush` 0.44 → 0.70, `Resonance` 0.46 → 0.35, `Echo` 0.25 → 0.55 | Overall level the same; the top end up, +1.5 to +4.8 dB from 8 kHz, as the bitcrusher fizzes |

The sound-free way also made the picture blockier, since `Crush` coarsens the pixelation. A
goal on light holds nothing else about the picture, and that is part of what a person is
choosing between.

**Free needs per-knob trust.** With nothing held, the solver first leaned on `Filter`, whose
slope is steepest. `Filter` rests mid-threshold in the hue formula, so every full step
overshot and the gesture stalled at a third of its goal.

Giving each knob its own trust region fixed it: a knob that carried a failed step is trusted
to a quarter of its move, and the step is solved again. The free gesture then reached its
goal in 29 s. The held gesture never had the problem, because holding the sound already
ruled `Filter` out.

**Finding the ways.**

1. Solve the goal, sparsely, as now.
2. Hold the knob that carried most of that answer at its resting value, and solve again.
3. Repeat until no way is left or a few have been found.

Each pass is a picture-only solve, about 30 s in the spike and needing to be under a second
in the editor. The first few ways can be worked out behind the first answer while the
person looks at it.

**Order.** Fewest knobs first, then smallest move. The order is fixed, so the same gesture
offers the same ways every time: unpredictable means not chosen by the person, never
random.

**Showing them.** The result strip names each way's knobs and what it does to the sound,
from a loudness and octave reading on a real render. "Another way" steps to the next and
back. Undo takes back the whole gesture, whichever way was showing.

**What stays held.** Held knobs sit out every way, as they sit out randomize. The loudness
guard applies to every way, since browsing sounds is the point and a jump of 10 dB is not.
