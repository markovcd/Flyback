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
alone, and a fifth reading was taken from the shipped catalogue.

**Validated, and kept unchanged.** `|>` with nesting-as-wiring, and
`call(socket: value)`. All four wrote them fluently from a handful of examples
and none reached for an alternative. Convergence means conventional, which is
the reason to keep them and not a reason to think them optimal.

**Confirmed agent-hostile.**

- Meaning that depends on which sockets you happened to name (§3 below).
- Vocabulary that must be recalled rather than fetched. The specimens invented
  `phase`, `window` and `stripes`, and reached for `divide`, `smooth`,
  `lowpass`, `above`, `wrap` and `previous` where the catalogue has `div`,
  `slew`, `filter`, `step`, `mod` and `feedback`.
- Nesting. Every specimen bound short pipelines to names and combined the names;
  none wrote the deep chains `print` emits.
- Silence on a mistake. `let` bound twice crashes with a raw guid; `let t = …`
  shadows the clock without a word.

---

## 3. The pipe lands where the catalogue says, and nowhere else

Today's rule has three clauses, and the third is the problem:

> 3. otherwise **the first free socket takes the first output**.

"Free" means *not named in this call*, so what a stage does depends on which
arguments were written. Two readings of the same module:

```
rings(freq: 3) |> color.hsv(saturation: 0.85, value: 1)     # lands on hue
rings(freq: 3) |> color.hsv(hue: h, saturation: s, value: v) # lands nowhere
```

The second is what an unsteered agent wrote — signal in, parameters named, which
is how every shader and effect chain it has ever seen behaves. It is currently an
error, which is the right answer arrived at by accident: the rule has no clause
for it.

**Proposal.** A module declares **one pipe socket** — a single socket, or a
position pair — and a pipe always lands there.

- Naming that socket in the same call is an error that says so.
- A module that declares none cannot be piped into; write it as a call.
- Clauses 1 and 2 survive as *declarations in the catalogue* rather than
  inferences at parse time. Clause 3 is deleted.

`osc.sine` declares `in`, `math.smoothstep` declares `in` (its third port),
Space modules declare their position pair, `color.hsv` declares `hue`. Every
preset that reads correctly today goes on reading correctly, because the
declaration is what the old rule was trying to guess.

The gain is that `a |> f(…)` means one thing, always, and an agent can know
which without simulating the rest of the line.

## 4. One canonical form

Three normalizations. Each is accepted loosely and written back strictly, so a
person keeps the sugar and an agent always reads the same dialect.

**Arguments are named.** `math.remap(in_low: -2, in_high: 2, out_low: 0,
out_high: 1)`. Positional arguments and the `..` range are accepted and
normalized away. Nobody counts sockets.

**Statements are flat.** A pipeline may not appear inside an argument. Instead
of `osc.sine(freq: steps |> audio.note())`, a `let` above and a name below. This
is what every specimen wrote unprompted, and it is what makes an edit a one-line
edit rather than a change of shape.

**Modules are full type ids.** `color.hsv`, `osc.sine`, `math.remap`. The
ambiguity table disappears, `midi.in` stops shortening to `in`, and a name in
the text matches a name in the catalogue exactly. Short names stay legal input.

The cost is verbosity, which is the trade being asked for. Plasma, whole:

```
let slowly  = t * 0.2
let wave_y  = y |> osc.sine(freq: 1.1, phase: slowly)
let crossed = x |> osc.sine(freq: 1.5) |> math.add(b: wave_y)
let level   = crossed |> math.remap(in_low: -2, in_high: 2, out_low: 0, out_high: 1)

level |> color.hsv(saturation: 0.85, value: 1) |> out.color
```

One binding per nesting that had to be lifted, and longer lines. Against the
form in §1 of the reference it is the same program — nine modules, nine wires,
twenty-nine picture ops, thirty-six registers — and every line of it builds
today, since only §3 needs a change to the catalogue.

## 5. `print` emits the canonical form

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

Shadowing is currently unspecified in all four of its forms, and each behaves
differently. All four become errors, beside the two guards the binder already
has for a duplicate `def` and a second `keyboard`:

| Written | Today | Proposed |
|---|---|---|
| `let a` twice | unhandled exception, raw guid | error, naming both lines |
| `let t = …` | silently replaces the clock | error |
| `let out = …` | accepted; a confusing error later | error |
| `let sine = …` | accepted | legal — §4 means modules are full ids, so there is no collision |

## 7. A patch says what it needs

```
flyback 1
requires flyback.picture
```

`Patch.Requires` already exists and is recomputed on write; writing it down makes
a `.fbks` self-describing. An agent reading a file learns which namespace it may
draw on, and `check` can say "this build has no `flyback.picture`" once instead
of "nothing here is called `circle`" five times.

Optional to write, always printed.

## 8. The panel is spelled

A `PatchControl` — a named panel knob, its range, and the MIDI CC it follows —
has no spelling at all today. The binder cannot build one and the printer drops
it, so a played patch printed and rebuilt is a different instrument.

```
panel depth = 0.5 in 0..1 cc 21

let wash = color.vignette(amount: depth)
```

A knob is a named value in the same namespace as a `let`, and a socket follows
one by naming it where a number would go. No ids, no indices, no coordinates —
which is the 0033 test.

## 9. A diagnostic is a repair instruction

`check --json` already exists. Each issue should carry a stable `code`, the span,
the message, and — where there is one — the replacement text, so a repair loop
never parses prose.

And a crash is a diagnostic that did not get written. There should be none.

## 10. The catalogue is fetchable, and complete

The cheapest item here, and not a language change at all.

`modules --json` is 11 KB for 77 modules — small enough to hand a model whole —
and carries type ids, categories and port names. It omits the four things needed
to write correct text:

- **the pipe socket** (§3), which is the single most load-bearing fact;
- **each port's display**, so `A3` and `20ms` are known to be legal there;
- **defaults**, so an agent knows what it may leave out;
- **what a module carries** — a step block, a file, a plugin's fields.

Add those and the invented-module failure mode mostly goes away, with no syntax
touched.

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
catalogue — though the existing rule computes the right answer for most, and those where
it differs are the ones that are silently wrong now. Every `.fbks` in the tree
and every snippet in `site/` rewrites to the canonical form. Sections 3, 4, 5 and
13 of `docs/language.md` are rewritten. The preset corpus test — build the text,
compile both, compare opcode for opcode — is what proves the pipe socket
declarations right, and it already exists.

## Open

- Whether `group` can survive `print` under §4. 0065 gives ordering as the
  reason it cannot: a binding is written the moment something first needs it, and
  a group's members are not contiguous in that order. One binding per node
  loosens exactly that constraint, so it is worth rechecking rather than
  assuming.
- Whether `def` earns its place. No specimen reached for it, and a macro hides
  what a call site costs. Four voices is the argument for keeping it.
- Whether `panel` belongs in the language or stays editor state.
