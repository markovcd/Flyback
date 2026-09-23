# ADR-0137: The clock reaches the GPU in two floats

**Status:** Accepted · 2026-09-23 · *user-directed* · carries out what
[0035](0035-a-glsl-backend-for-the-video-path.md) deferred

## Context

The shader was handed the clock as one `float`. A float of t steps by a quarter
of a millisecond an hour in, by 31 ms three days in and by two seconds a year in,
so a picture on the GPU stairstepped after an hour, juddered after a day and froze
between jumps after a year, while the same patch on the processor ran smoothly.
ADR-0035 recorded this and named the fix: a two-float time uniform with a Dekker
product.

Carrying only the clock is not enough. A tempo is `bpm × 1/60`, two constants the
interpreter multiplies in double and a float rounds to 24 bits. That rounding is
a relative error of 6e-8, and multiplied by a year of seconds it is five beats:
the song is somewhere else and so is everything drawn from it.

## Decision

**The clock arrives as `uTime` plus `uTimeLo`,** the double split into the float
it rounds to and what is left over.

**A register is carried as a `vec2` pair when it is the clock, when it is
arithmetic on a pair, or when it is arithmetic feeding a pair.** The first two
are found walking forward; the third walking back, so the constants and knobs
behind a rate are multiplied exactly before the clock meets them. Add, subtract,
multiply, divide, `Phase`, negate, `abs`, `floor` and `ceil` keep a pair a pair.

**The ops that throw the size away do it in two floats:** `fract`, `mod`, `sin`,
`cos`, `tan` and noise. A sine takes off whole turns against 2π held in two
floats of its own. Noise finds its lattice point in two floats and wraps it to 32
bits as `Noise.Lattice` does. Every other op reads the pair as the one float it
rounds to, which is what it read before.

**The error-free sums and products multiply by `uOne`, a uniform the renderer
sets to 1.** ANGLE's HLSL compiler folds `(a + b) - a` into `b` otherwise, which
is the whole of what a two-sum computes; a value it cannot see stops it.

## Consequences

On this machine's GPU through ANGLE, Plasma and Overworld match the processor as
closely a year in as they do at twenty seconds, under a fifth of a level of 255
on average; before, they were 73 and 12 levels apart. A patch drawing `fract(t × 11333)` is exact a year in,
where it was 188 levels out.

A pair costs a few dozen flops per op where a float cost one, paid only on the
lines the clock reaches: 43 of Fracture's 937 and 75 of Overworld's 1345.

The interpreter is still the specification, and two floats hold about 48 bits
against its 53: close enough that nothing the eye can resolve differs, not
identical. A lattice index that lands on an integer can still floor differently.

A hash built as `fract(sin(n) × 43758.5)` still differs between the two, at
nought seconds as much as a year in, because a GPU's `sin` is not correctly
rounded and the multiplication magnifies it. That is not a clock problem, and
this does not touch it: a preset that wants a hash reads Noise at whole lattice
points, which is the engine's integer hash on both, as Fracture's sparks do.
