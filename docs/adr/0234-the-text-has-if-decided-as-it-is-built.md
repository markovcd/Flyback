# ADR-0234: The text has `if`, decided as it is built

**Status:** Accepted · 2026-11-16 · *user-directed* · extends
[0208](0208-the-text-has-loops-that-unroll-as-it-is-built.md)

## Context

A def with an option, a reverb or a darker filter, could only switch it off by
multiplying it by zero, which still places the modules and pays for them on
every sample. A loop pass that should differ from the others, every fourth step
or the last voice, had to be split out of the list by hand.

## Decision

**`if condition { ... } else { ... }` builds one branch, chosen as the text is
built.** The engine never sees it: the patch is the modules the branch placed,
the same as that branch written out.

```
def voice(pitch, dark = 0) = {
  let tone = saw(freq: note(pitch))
  if dark { tone |> filter(cutoff: 600) } else { tone }
}

for k in 1..8 {
  if k % 4 == 0 { kick() } else { hat() }
}
```

- **The condition reads only what the text writes down**: numbers, notes,
  durations and texts, and the names bound to them. A signal, a list or a panel
  knob is refused, the refusal pointing at `mix(a, b, step(0.5, signal))` for a
  choice made while the patch plays.
- **Comparisons are `== != < <= > >=`**, joined by `and` and `or` and turned
  round by `not`. A plain number holds where it is not zero; texts compare only
  for equality. A comparison anywhere but a condition is refused, so the
  language gains no boolean values.
- **`else` is optional, may start the next line, and chains as `else if`.** A
  branch is a block like a loop's body, and an `if` with no branch taken is
  worth an empty list.
- **`if`, `else`, `and`, `or` and `not` are words of the language** and cannot be
  bound as names.

There is no `if` on a signal. Every op of a program runs on every evaluation, so
a per-sample `if` would be `mix` and `step` by another spelling, and would read
as if it saved the branch not taken.

## Consequences

- A printing writes out the branch that was taken; the other is lost, as a loop
  is unrolled (0065).
- A number a condition reads through a name is read twice where a knob reads it
  too, so turning that knob never rewrites it (the rule for any number read
  through a name twice). A knob on the canvas never changes which modules exist.
- The modules of a branch are named by the `if` and the branch, so a rebuild keeps
  their identities and the two branches never share one.
