# Grab the output: a patch that knows which knob moves what

Planned on 2026-10-01, on `main` at `9d667a68`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked

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
