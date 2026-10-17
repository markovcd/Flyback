# ADR-0208: The text has loops, unrolled as it is built

**Status:** Accepted · 2026-10-17 · *user-directed* · extends
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

A patch with the same voice four times, or the same line spoken six times at six
places in a song, is written out four or six times in the text. The C# presets
loop instead, which is half of why they are still C# while every user patch is
text. `def` takes the body out of the repetition but leaves a call per copy, and
nothing in the language can hold the table the copies differ by.

## Decision

**`for` stamps out its body once per item, as the text is built, and hands back
a list.** The engine never sees a loop: the patch is the modules the passes
placed, the same patch as the text written out by hand.

```
let lines = [("sleepy-voice.wav", 0.5), ("one-side.wav", 7.5)]

let spoken = for (file, at) in lines {
  (phrase - at) / rate |> sample(file)
}

sum(spoken) |> out.left
```

- **Lists.** `[a, b, c]` in place of a value, items separated by commas, each a
  number, a note, a text, a signal or a bracketed tuple. A `[` straight after a
  call's `)` is still that module's block.
- **`for name in list { ... }`**, or `for (a, b) in list` to take each item's
  tuple apart. The source may be a range of whole numbers, `1..4`, which counts
  both ends. The body is a def's: statements, then what each pass is worth.
  Names bound inside a pass are that pass's; the names outside are in sight. A
  `for` with nothing at the end of its body is worth nothing and only places.
- **`...list`** spreads a list into a call's free sockets in order, a tuple's
  parts each taking one: `mixer(...for v in voices { (v, 0.5) })`.
- **`sum(list)`** adds the items up, as `a + b + c` would, one Expression for the
  lot. It is a word of the language only where no module, def or binding is
  called `sum`, so no patch that already uses the name changes.
- **A text bound to a name is a file**, wherever the literal would be: a loop
  variable or a def parameter can name the sample a module plays.

Only lists that are written down can be looped over, and nothing recurses, so
every text still builds in time proportional to what it places. A pass's modules
are named by the loop and the pass's index, so a rebuild keeps their identities
as it keeps a def call's.

## Consequences

- A printing writes a looped patch out unrolled: printing is lossy (0065), and a
  loop is one more thing it does not recover.
- A number written once in a list may stand for a knob on every pass. Turning
  one of those knobs on the canvas writes back to the list, which turns them all.
- Arithmetic on a loop's numbers folds where it is `+ - * / %` between numbers;
  a function of a number (`sqrt(k)`) still places an Expression.
