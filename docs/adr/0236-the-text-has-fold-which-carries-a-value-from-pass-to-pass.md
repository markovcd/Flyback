# ADR-0236: The text has `fold`, which carries a value from pass to pass

**Status:** Accepted · 2026-11-16 · *user-directed* · extends
[0208](0208-the-text-has-loops-that-unroll-as-it-is-built.md)

## Context

A pass of a `for` loop sees the names outside the loop and its own item, never
what the pass before it made. So an iteration, where each step starts from the
last one's answer, cannot be written: a ray march, an escape-time fractal, a
chain of filters each fed by the one before. The C# modules that do these
(Raymarch, Mandelbrot, Plate) build their chains with a C# `for` loop, which is
the main reason they cannot be written as text.

## Decision

**`fold list from seed with name { body }` stamps the body out once per item,
each pass reading by `name` what the pass before ended with**, the first pass
reading `seed`. The fold is worth what the last pass ends with.

```
let tone = fold k in 1..4 from sine(freq: 110) with sound {
  sound * 0.5 + sine(freq: 110 * k)
}

let (a, b) = fold 1..8 from (0, 1) with (a, b) { (b, a + b) }
```

- **It is a `for` with a carry**: the same list or count, the same optional item
  names, the same pass levels and identities, the same limit of 4096 passes. The
  binder runs both through one loop.
- **A carried tuple is named in brackets** and every pass must end with as many
  things; a pass that ends with nothing is refused.
- **`fold` is a word only where a list, a count or a pass's names follow it**, so
  the Fold module's `fold(drive: 2)` and a `let fold` still read as names. `from`
  and `with` are words only inside a fold.

## Consequences

- The program is the unrolled chain, exactly as the C# modules build theirs, so
  nothing loops while the patch plays and a fold costs what its copies cost.
- A printing writes the chain out unrolled, as it does a loop (0065).
- A fold is the language piece a data-only module pack would most need; the rest of
  that idea is planned in `docs/handoff/module-packs.md`.
