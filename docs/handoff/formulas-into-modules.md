# What the presets still write as formulas, and which modules would replace it

Surveyed on 2026-09-28, on `main` after every showcase moved onto the Arrangement
(ADR-0153). The Arrangement took the biggest family of hand-written formulas out: an
intensity lane decoded per part with `step` and `smoothstep` thresholds. This is what
is left and which of it is worth a module. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last port it lists.

## How much is left

Every shipped preset was printed as text (`flyback-cli print --preset <name>`, with
the Effects, Voice, Picture, Mastering, Easy, Figures and Fractals plugins in the
CLI's `plugins/`), plus Tranquility from `worker/defaults/`. A preset
built in C# arrives fused (ADR-0108), so its `Times`/`Plus`/`Product` chains print
as formulas too and the count sees them. 498 formulas, about 1,600 operators and
calls, in 15 of 58 presets. Counting `+ - * / %` and each call as one:

| Preset | Formulas | Operators and calls | Formulas of six or more |
|---|---|---|---|
| Tranquility | 97 | 310 | 17 |
| Dodge | 17 | 293 | 14 |
| Overworld | 56 | 237 | 14 |
| Irrational | 31 | 94 | 7 |
| Vigil | 22 | 89 | 5 |
| Fracture | 30 | 82 | 5 |
| Slow weather | 30 | 77 | 4 |
| Phase | 32 | 73 | 4 |
| Bronze | 38 | 69 | 1 |
| Mycelium | 31 | 64 | 2 |
| Outrun | 25 | 56 | 2 |
| Whole band | 25 | 42 | 3 |
| Warehouse | 11 | 39 | 3 |
| No Sense Dub | 17 | 32 | 0 |
| Acid | 14 | 28 | 1 |

Calls by frequency: `step` 100, `smoothstep` 53, `fract` 45, `floor` 40, `mod` 29,
`abs` 28, `sin` 15, `pow` 14, `min` 14, `max` 13, `clamp` 12, `mix` 7, `exp` 7.

## The shapes that repeat

ADR-0097 is the bar: a wrapper is counted across presets before it is built, and a
pair of modules does not earn a name.

| Shape | Uses | Where | Verdict |
|---|---|---|---|
| On between two points: `step(a, x) * (1 - step(b, x))`, the same with `smoothstep`, `step(abs(x), w)` | 13, and more the pattern match misses | Dodge 4, Overworld 3, Phase 2, Tranquility 2, Vigil 2, Slow weather | **Build a Window.** |
| A place in a section: `step(0.875, progress)`, `(section - 1) % 4`, `fract(a / 64)` | 16 | nine presets | No module: a Window on the Arrangement's `progress`, or a row. |
| A decay off a phase: `pow(1 - fract(x), n)`, `exp(-k * phase)` | 12 | Irrational 6, Dodge 3, Whole band 2, Slow weather | No module: it is the Stroke. |
| A stable random for a step: `fract(n * 53.1 + m * 7.9)`, Dodge's `mod(… , 997)` chains | 7, and Dodge's three largest formulas | Tranquility 4, Mycelium 2, Fracture, Dodge | **Build a Dice.** |
| Octaves to hertz: `base * pow(2, x)` | 6 | Tranquility 4, Acid, Dodge | An exponential mode on something existing, not a module. |

## Landed

Window (`in`, `from`, `to`, `rise`, `fall`: a Smoothstep pair, the Threshold pair
where `rise` and `fall` are nought) and Dice (`in`, `seed`, `sides`: Dodge's quadratic
modulo 997, out 0..1 and a `face`) are Maths modules, so a formula calls them as
`window()` and `dice()`. These were ported and compare bit for bit over each piece:

- Dodge's gap as `floor(dice(a) * 7)`, its field and walls as `window(x, -0.91, 0.91)`.
- Phase's twelve cells of a row, a Window from -0.5 to 11.5.
- Vigil's swell and lift, two Windows of unequal rise and fall.
- Fracture's second theme, a Window from section 9 to 13.

## Left: the ports that change what a preset plays

Each of these is a near miss, not the same numbers, so it is a change to hear and see:
listen, and retake the site's shots and tracks where the preset has them.

- Tranquility's subtitles, `smoothstep(0.1, 0.3, a) * (1 - smoothstep(5.3, 5.6, a))`:
  a Window from 0.1 to 5.6 rising 0.2 and falling 0.3, off by the rounding of 0.1 + 0.2.
  Tranquility is a `.fbkb` in `worker/defaults/` (the `preset-site-defaults` skill).
- Overworld's HUD bar and thin lines, `1 - smoothstep(0.019, 0.021, abs(b - 0.925))`:
  symmetric about a center, so a Window is the same band with different rounding.
- Vigil's bells and knells, `step(100, a) * step(a, 245)`: on at the far end too, where a
  Window is off, which differs on the one sample that lands exactly on it.
- Tranquility's chatter: the ratio `fract((a * 0.5 + 0.5) * 37.7 + b * 11.3)`, the
  vowel `fract(... * 19.1 + ... * 71.7)` and the pitch jitter, each a new random from
  two held noises.
- Mycelium and Fracture: `fract(cells.cell * 13.7 + euclid.index * 5)` to pick which
  cells light, a `dice(cell, index)`.

### Why not the others

- **Section cues.** Most are "the last bar" (a Window on `progress`) or "every fourth
  section" (a row). The ports wrote the second as `step(3, (section - 1) % 4)` to
  save the ops a row costs; a row reads better, and the cost is about two ops a
  change of level.
- **Decays.** The Stroke is this, and the presets that use it already do. What is
  left is Whole band, an engine preset that cannot reach the Voice plugin's Stroke,
  and Irrational's `pow(1 - abs(fract(x - y + 0.5) - 0.5) * 2, 6)`, a pulse
  symmetric about each coincidence, six times in one preset.
- **Octaves to hertz.** Six uses in three presets. Remap is linear and has no mode
  to hang it on (`NodeCatalog.Maths.cs`); a taper on Remap, or a knob on the Note
  module, would take these without a new name.

## What stays a formula

Dodge's game logic (score, lanes, collisions) and Tranquility's chatter are written
once, for one piece, and read as what they do. Overworld's picture arithmetic is
what ADR-0104 made the Expression for.

## How to measure it again

Print every preset as above, collect each `let` whose right side is arithmetic and
each `formula: "..."`, and count operators and calls per formula. The shapes above
were matched with regular expressions over that list, so treat a count as a floor.
