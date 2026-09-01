# ADR-0065: A text language, which parses to a patch

**Status:** Accepted (user-directed) · 2026-09-01 · implemented in
`Flyback.Core/Language`; takes up the option
[0004](0004-visual-patch-editor-as-the-authoring-model.md) declined and left
open; constrained by [0019](0019-no-third-party-dependencies-in-the-engine.md)
and [0020](0020-json-patch-files-keyed-by-string-type-ids.md)

## Context

[0004](0004-visual-patch-editor-as-the-authoring-model.md) put three authoring
models to the project owner — a composable C# graph, a visual patch editor, and
a text DSL in the style of Hydra or TidalCycles — and the editor won. Its own
consequences left the third one standing:

> The option not taken is not foreclosed. `PatchBuilder` gives a fluent C# API
> over the same graph, and the presets are written with it — so patches *can* be
> built in code, they just are not the primary route. A text DSL would be a
> parser producing a `Patch`, which is a self-contained addition.

Three things have accumulated since which argue for building it.

**The presets are 2102 lines of graph assembly, and they address ports by
number.** `Presets.Plasma` is eight modules and nine wires written as thirty
lines of `b.Add(...)` and `b.Wire(coord, 0, horizontal, 0)`. Reading it means
counting sockets in a different file. The patches that ship are the worked
examples, and they are written in the least legible form the project has.

**One socket kind is a standing trap.** A `PortDisplay.Duration` knob holds
log₁₀ seconds, and the presets carry hand-written comments to survive it —
`// so -2.4 is about four milliseconds`, `// so this is 10^-1.7`. That is a
comment doing a type's job, twice, in the file that teaches people how to patch.

**A textual patch notation already exists and is load-bearing.**
`PatchWorkbench.DescribePatch` prints handles, port *names* and arrows for the
assistant ([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)),
and the reason given there is that a model "asked to invent twenty consistent
guids, or to count a Sequencer's twenty-one inputs, will get it wrong". Every
word of that is also true of a person reading a diff.

## Decision

**A pipeline language, parsing to a `Patch` and to nothing else.** A signal is
written left to right through `|>`; nesting is wiring. There is no second
engine, no interpreter and no runtime — the parser's whole output is the same
object `PatchIO` reads. The reference is [`docs/language.md`](../language.md).

**`build` is exact; `print` is lossy.** Text to patch is authoritative and
deterministic. Patch to text is best-effort, for reading, diffing and sharing,
and it drops node ids, canvas positions, and whether a group is collapsed. The
asymmetry is chosen rather than conceded: guaranteeing byte-identity both ways
would force the syntax to carry coordinates, which is the one thing nobody wants
to write by hand.

**A module is named by the last segment of its type id, never by its label.**
`osc.sine` is `sine`. [0020](0020-json-patch-files-keyed-by-string-type-ids.md)
guarantees a type id is stable and says a module may be renamed in the UI
without touching saved patches; a language keyed to the label would break every
text patch the next time a caption improved. Ambiguity is a parse error resolved
by writing the id in full, and across all ninety modules in the box there are
exactly two collisions — `hsv` and `mix`.

**The catalogue is the language.** Short names, port names, arities, and which
literals a port accepts are all read out of `NodeCatalog` at parse time. A
plugin's modules are usable the moment it loads, and there is no alias table
that can go stale.

**The sugar is a short, hand-written list**: `x`, `y`, `radius`, `angle` and
`t`; infix arithmetic for the five binary maths modules; note literals (`A3`);
duration literals (`20ms`); and ranges (`-2..2`). The note and duration literals
are driven by `PortSpec.Display`, so they reach a plugin's ports without the
plugin declaring anything.

**The step block borrows TidalCycles' mini-notation**, and expands at parse time
into a flat `List<Step>`. No module, no opcode, and no engine change: `~`,
`[a b]`, `@n`, `!n`, `<a b>` and Euclid are all rewriting.

**`def` is macro expansion, and `group` is a box.** A `def` stamps out its body
at each call site before any `NodeInstance` exists, which is how a text patch
gets the reuse that [0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md)
explicitly denies a `NodeGroup` — a group cannot be instantiated twice because a
`Connection` names ports by index, and a macro never reaches that stage.

**It is a hand-written recursive-descent parser.**
[0019](0019-no-third-party-dependencies-in-the-engine.md) forbids third-party
dependencies in the engine, and `Flyback.Core` currently carries zero
`PackageReference` entries. There is no Superpower, Pidgin, Sprache or ANTLR
anywhere in the solution, and none is being added.

## Consequences

**The design was verified against the whole preset corpus before any code was
written, and that changed four things.** Every shipped preset is transliterated
in the reference, and doing it by hand found the holes while they were still
cheap to move:

- The pipe rule as first drafted put the signal in `edge0` on Smoothstep and
  `edge` on Threshold, because those two modules take their input last. Three
  presets would have compiled, rendered, and quietly meant something else. The
  rule now prefers a port named `in`.
- `def` had to be able to hand back more than one value, because Four voices —
  the patch that motivated macros at all — has a fader shared between a voice
  and a band, and a single-pipeline body would have duplicated it.
- `group` had to exist, because Whole band organises about a hundred modules
  into ten of them, and a text form that dropped them would leave the patch that
  most needs reading the least able to be read.
- Literal arithmetic has to constant-fold, because In key sets a knob to `1/12`.

Finding these on paper is the whole argument for specifying before implementing,
and it is why this record was written before a parser was.

**Building it found three more, and one of them was the pipe rule again.** The
parser is about 1,400 lines in `Flyback.Core/Language`, and every preset is
tested against the C# one it transliterates — the same instrument compiled to
the same program, opcode for opcode and register for register. Standing every
one of the twenty up against that is what turned these up:

- **"Forward every output a source has" is wrong.** It reads well on geometry,
  where a Space module hands its `(x, y)` to the next, and it is a disaster
  everywhere else: `steps |> note()` put the sequencer's `gate` into Note's
  `octave` and its `index` into the `cents`. The rule is now that a socket named
  `in` takes one signal, a **position** — a leading `x` and `y`, which is what
  [0050](0050-normalled-sockets-carry-a-signal-with-no-wire.md) already treats as
  a pair — takes two, and everything else takes one.
- **The range binds looser than the minus.** `-2..2` had been parsing as the
  negation of `2..2`, which is the same class of mistake as the `edge0` one and
  was caught the same way.
- **A sharp and a comment are the same character.** `C#4` read as the name `C`
  followed by a comment that swallowed the rest of the line. The note is now
  matched before the identifier, which costs nothing because nothing else in the
  language puts a `#` inside a word.

The first of those is the one worth the record: it is a rule that was wrong in
the *specification*, survived being written out by hand across twenty patches,
and was caught only by compiling both and comparing. A reviewer would not have
seen it, and neither did the author.

**A rest is not a silenced note**, and the tests are where that became clear.
`~` carries no pitch; `E5%0` carries its own and no volume. They sound identical
and compile differently, and Whole band's lead is written with the second — so
the notation has to be able to say both, and the reference now says which is
which.

**Two things the language deliberately does not hide.** A duration that is
computed rather than set stays in decades, because the `ms` literal is sugar for
a knob and a Remap's output range is an ordinary Number port. And a pipe into an
oscillator lands on its domain rather than its pitch, which is the distinction
the assistant's handbook spends five paragraphs on and which the language has no
business papering over. Both are places where the sugar stops exactly where the
arithmetic starts.

**A second authoring surface is a second thing to keep in step**, and it is
cheaper than it looks because almost nothing is duplicated. Module names, port
names and literal kinds are read from the catalogue rather than restated, so
adding a module makes it available in the language with no edit here. What must
be maintained by hand is the sugar table and the mini-notation — both short, and
both exercised by the transliterated presets.

**The relationship to TidalCycles is a borrowing, not a port**, and the reference
says so plainly so that nobody arrives expecting the rest of it. Tidal is an
event scheduler and its power is in transforming lists of events; this is a flat
register machine ([0005](0005-compile-to-a-flat-register-machine.md)) where a
wire carries one or three floats. So `every`, `jux`, `off` and `sometimesBy`
have no expression here — nothing on a wire can carry a pattern — and `<a b>`
costs ops where Tidal's costs nothing, because it is unrolled rather than
scheduled. What is taken is the step notation, which is the part that maps.

**Time stays in seconds, and that is the largest remaining difference in feel.**
Tidal is cycle-relative throughout. A beat-relative literal here would need a
tempo in lexical scope, which is precisely the hidden global
[0048](0048-time-is-seconds-and-nothing-else.md) refused when it took the rate
knob off Time — so a patch that wants beats wires a `tempo` module, visibly.
This is the decision here most likely to be revisited, and it should be
revisited as an amendment to 0048 rather than quietly as a parser feature.

**Nothing about the editor changes, and neither surface is primary.**
[0004](0004-visual-patch-editor-as-the-authoring-model.md) stands: a patch is a
graph, the canvas is how it is built, and this is a second view of the same
artefact. A `let` name becomes `NodeInstance.Name`, so a patch built from text
opens on the canvas already labelled, and one built on the canvas prints back
carrying the names somebody chose — which is where the two surfaces stop being
rivals and start being views.

**Where it lives is not decided here.** The parser belongs in `Flyback.Core`
beside `PatchIO`; whether it is reached through a CLI verb, a text pane in the
shell, or a tool the assistant can call is a separate decision, and each of the
three is additive. This record settles the language and not its front door.

**The file extension is `.fbs`**, beside `.fbk` for the document and `.fbkp` for
the bundle ([0060](0060-a-bundle-is-a-patch-and-what-it-names.md)). A `.fbs` is
a source: it names the modules it uses but carries none of the files they name,
so packing one means building it first.
