# A Flyback language an agent writes as well as a person

A draft, not a decision. [0065](adr/0065-a-text-language-that-parses-to-a-patch.md)
settled the language; this proposes what changes if the priority is stated
outright: **an agent must write it as well as a person, and where the two
conflict, the agent wins.**

That priority is not new. 0065 built the language because an assistant placing
Whole band one module at a time needed 222 tool calls against a budget of 200,
and [0033](adr/0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)
had already found what a model is good at: *"Handles and port names are what the
model is good at; guids, indices and coordinates never cross the boundary at
all."* What follows takes that further than either record did.

---

## 1. The thesis

> **Nothing is inferred from what you did not write.**

Everything below is that sentence applied.

A model fails at text in three ways, and only the third one matters here.

- It writes something that does not parse. Harmless: `check` says so.
- It writes a name that does not exist. Nearly harmless, and fixable by handing
  it the vocabulary.
- **It writes something that parses, compiles, and means something else.** There
  is no feedback at all, and both the model and the person reading the diff see
  correct-looking text.

The third is not hypothetical here. 0065 records the pipe rule being wrong twice
in exactly this way — once putting a signal into `edge0`, once forwarding a
sequencer's `gate` into an octave — and says of the first:

> it is a rule that was wrong in the *specification*, survived being written out
> by hand across twenty patches, and was caught only by compiling both and
> comparing.

A rule that a careful human misses twenty times is a rule a model misses every
time. The design goal is to leave no construct whose meaning depends on
something absent from the line.

## 2. What the evidence says

Four unsteered agents wrote about 1400 lines of Flyback text from the website
alone, and a fifth reading was taken from the shipped catalog.

**Validated, and kept unchanged.** `|>` with nesting-as-wiring, and
`call(socket: value)`. All four wrote them fluently from a handful of examples
and none reached for an alternative. Convergence means conventional, which is
the reason to keep them and not a reason to think them optimal.

**Confirmed agent-hostile.**

- Meaning that depends on which sockets you happened to name (§3 below).
- Vocabulary that must be recalled rather than fetched. The specimens invented
  `phase`, `window` and `stripes`, and reached for `divide`, `smooth`,
  `lowpass`, `above`, `wrap` and `previous` where the catalog has `div`,
  `slew`, `filter`, `step`, `mod` and `feedback`.
- Nesting. Every specimen bound short pipelines to names and combined the names;
  none wrote the deep chains `print` emits.
- Silence on a mistake. `let` bound twice crashes with a raw guid; `let t = …`
  shadows the clock without a word.

---

## 3. The pipe lands where the text says, and nowhere else

**Done, without the catalog.** The rule had a third clause, "the first socket
the call did not name takes the signal", so what a stage did hung on the
arguments beside it: `hsv(saturation: 0.85, value: 1)` after a pipe took it on
`hue`, and `hsv(hue: h, saturation: s, value: v)`, which is what an unsteered
agent wrote, took it nowhere.

The clause is gone. A pipe lands on `socket: _` where the call writes one, else
on `in` or a module's only socket, else on a leading `x` and `y` pair, else on
a module's one color socket for a color, and anything else is an error that
says to write `_`:

```
rings(freq: 3) |> color.hsv(hue: _, saturation: 0.85, value: 1)
beat.gate |> env.adsr(gate: _, decay: 240ms)
```

This was proposed as a pipe socket each module declares. `_` gets the same
guarantee with no catalog pass: `a |> f(…)` means one thing, and an agent reads
which from the line.

## 4. One canonical form

**Declined, but for flat statements.** A pipeline inside an argument is an
error, as below. The other two are not taken: the printer follows a patch's own
chain, writes its arithmetic as sums and names a binding after what it drives,
and one binding per module in full type ids would undo all three for a dialect
nobody reads more easily. Named arguments are already what it writes; short
names are what everybody writes.

Three normalizations were proposed. Each is accepted loosely and written back strictly, so a
person keeps the sugar and an agent always reads the same dialect.

**Arguments are named.** `math.remap(in_low: -2, in_high: 2, out_low: 0,
out_high: 1)`. Positional arguments and the `..` range are accepted and
normalized away. Nobody counts sockets.

**Statements are flat.** Done: a pipeline inside an argument is an error. Instead
of `osc.sine(freq: steps |> audio.note(note: _))`, a `let` above and a name
below. This is what every specimen wrote unprompted, and it is what makes an
edit a one-line edit rather than a change of shape.

**Modules are full type ids.** `color.hsv`, `osc.sine`, `math.remap`. The
ambiguity table disappears, `midi.in` stops shortening to `in`, and a name in
the text matches a name in the catalog exactly. Short names stay legal input.

The cost is verbosity, which is the trade being asked for. Plasma, whole:

```
let slowly  = t * 0.2
let wave_y  = y |> osc.sine(freq: 1.1, phase: slowly)
let crossed = x |> osc.sine(freq: 1.5) |> math.add(a: _, b: wave_y)
let level   = crossed |> math.remap(in_low: -2, in_high: 2, out_low: 0, out_high: 1)

level |> color.hsv(hue: _, saturation: 0.85, value: 1) |> out.color
```

One binding per nesting that had to be lifted, and longer lines. Against the
form in §1 of the reference it is the same program — nine modules, nine wires,
twenty-nine picture ops, thirty-six registers — and every line of it builds.

## 5. `print` emits the canonical form

**Declined**, with §4. What `print` writes still reads back as the same program,
and every line of it is legal input.

Not byte-identity — 0065 chose the lossy asymmetry deliberately, because
guaranteeing both directions would force canvas coordinates into the syntax.
This is a smaller claim: **what `print` writes is the dialect an agent writes.**

Today it is not. The printer inlines everything it can and emits a `let` only
when forced, so printed text is dense where written text is flat, and today
`math.remap(-2..2, 0..1)` — §1's own Hello example — comes back as
`math.remap(in_low: -2, in_high: 2)`.

Under §4 the printer gets simpler: one binding per node, named arguments, full
ids. An agent reads a patch, changes one line, writes it back, and never changes
dialect. A person who wants the dense form still writes it.

## 6. A name is bound once

**Done.**

| Written | Is |
|---|---|
| `let a` twice, in a group or a tuple too | error, naming the line of the first |
| `let t = …`, `let x = …`, `def f(x)` | error: the word is already the clock or a coordinate |
| `let out = …` | error |
| a socket wired twice (`\|> out.color`, `<-`) | error, naming the line of the first |
| a knob set twice (`a.freq = 3` after `sine(freq: 2)`) | error, naming the line of the first |
| `let sine = …` | legal — §4 means modules are full ids, so there is no collision |

`check --json` reports text that does not build with its line, column and
code (§9).

## 7. A patch says what it needs

```
requires flyback.picture
```

**Done, and the version line declined**: `requires flyback.picture` is read before
anything else and always printed. `Patch.Requires` is recomputed on write;
writing it down makes a `.fbks` self-describing. An agent reading a file learns which namespace it may
draw on, and `check` can say "this build has no `flyback.picture`" once instead
of "nothing here is called `circle`" five times.

Optional to write, always printed. A `flyback 1` line was declined: the
language keeps no old spelling readable, so a number that never changes would
only be one more line on every printing, and `requires` already says what a
file needs.

## 8. The panel is spelled

**Done**, spelled as the reference's section 9 has it: `panel cutoff = 0.4, cc: 21,
device: "…"`, and a socket follows it by naming it, over its own range or one
given, `cutoff(200..4000)`. The range belongs to each socket rather than to the
knob, which is what the engine keeps, so a played patch printed and rebuilt is
the same instrument.

```
panel depth = 0.5, cc: 21, device: "midi:launchkey-49"

let wash = color.vignette(dark: depth)
```

A knob is a named value in the same namespace as a `let`, and a socket follows
one by naming it where a number would go. No ids, no indices, no coordinates —
which is the 0033 test.

## 9. A diagnostic is a repair instruction

**Done, without fixes.** Each complaint about the text carries a stable `code`
(`IssueCode`), its line and column and the message. The assistant's
`write_patch` refuses with the same codes, in brackets. A `fix` carrying the
nearest name for a misspelling was tried and taken out: the nearest name was
often a different binding (`base` offered as `bass`) or one tied with another
(`adr` as `add` rather than `adsr`), and a repair applied blindly then wired
the wrong module without a word.

And a crash is a diagnostic that did not get written. There should be none.

## 10. The catalog is fetchable, and complete

The cheapest item here, and not a language change at all.

`modules --json` is 11 KB for 77 modules — small enough to hand a model whole —
and carries type ids, categories and port names. `modules <module> --json` adds
the four things needed to write correct text:

- **the pipe socket** (§3), which is the single most load-bearing fact;
- **each port's display**, so `A3` and `20ms` are known to be legal there;
- **defaults**, so an agent knows what it may leave out;
- **what a module carries** — a step block, a file, a plugin's fields.

Each socket also carries its help, the words the inspector shows as its tip.

With those one call away, the invented-module failure mode mostly goes away,
with no syntax touched.

---

## What is kept

`|>` and nesting-as-wiring, and `call(socket: value)` — both validated above.
Note and duration literals, which are the log₁₀ trap 0065 was written to close,
and which round-trip correctly today. The step mini-notation, which Whole band
needs and which is rewriting rather than runtime. `def`, `group` and `<-`, which
are needed and merely undocumented — none appears anywhere in `site/`, and two
specimens hand-rolled substitutes for them, one organizing a 297-line piece with
banner comments and the other shipping paste-in files.

Seconds rather than cycles stays, and stays out of scope: 0065 says a
beat-relative literal is an amendment to
[0048](adr/0048-time-is-seconds-and-nothing-else.md) and not a parser feature.
So do the hand-written recursive-descent parser
([0019](adr/0019-no-third-party-dependencies-in-the-engine.md)) and keying to
type ids rather than labels
([0020](adr/0020-json-patch-files-keyed-by-string-type-ids.md)).

## What it costs

A `NodeDef` field for the pipe socket and a pass over every module in the
catalog — though the existing rule computes the right answer for most, and those where
it differs are the ones that are silently wrong now. Every `.fbks` in the tree
and every snippet in `site/` rewrites to the canonical form. Sections 3, 4, 5 and
13 of `docs/language.md` are rewritten. The preset corpus test — build the text,
compile both, compare opcode for opcode — is what proves the pipe socket
declarations right, and it already exists.

## Open

- Whether `def` earns its place. No specimen reached for it, and a macro hides
  what a call site costs. Four voices is the argument for keeping it.
