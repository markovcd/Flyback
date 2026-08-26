# ADR-0054: What a module carries is a part, not a subtype

**Status:** Accepted · 2026-08-26 · *user-directed* · generalises
[0038](0038-a-sequencers-notes-are-a-list-on-the-node.md),
[0051](0051-a-quantisers-scale-is-a-set-on-the-node.md) and
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) from three
cases into one mechanism

## Context

Three times now a module has needed to carry something that is not a knob: a
sequencer's notes, a quantiser's scale, a player's file. 0051 saw the pattern
forming and said so — "the pattern is now a rule rather than an exception ... the
next one will be easier to argue for than either — which is worth being wary of".
0052 made it three.

The test those records set has held, and stays: the data is a decision *about*
the piece rather than a signal *in* it, **and** there is no arrangement of
sockets that expresses it. What did not hold is the shape. Each kind was added by
naming it everywhere it mattered, and by the third the same three-way `if` had
been written out in six places — `NodeDef` declaring it, `NodeInstance` storing
it, `NodeInstance.Create` seeding it, `PatchClipboard` copying it, `PatchCompiler`
lowering it, the inspector editing it, and the assistant describing it twice.

`NodeDef` had eight members for three kinds, four of them for one: `DefaultSteps`
carried the notes, `StepDisplay` and `StepRange` said how one reads, and
`StepValue` built a `PortSpec` out of the two. Three of those meant nothing on
the forty modules that have no steps, and `StepRange` defaulted to `(0, 1)` on
every one of them — a state that was legal and meaningless.

Worse, "does this module have notes" was being asked as `DefaultSteps is not
null`. A predicate smuggled into a nullable default is not a question anything
can answer wrongly, but it is one nothing can answer *deliberately*.

## Decision

**A kind of carried state is a part of a definition, not a subtype of one.**
`NodeDef` holds `IReadOnlyList<NodeExtra> Extras`, empty for the great majority
of modules. Each kind is one `NodeExtra` — `StepsExtra`, `ScaleExtra`,
`SampleExtra` — in one file, and the shared code iterates rather than enumerates.

**Subtyping was considered and refused**, though it is the more obvious reading
of "these modules have extra fields". Three reasons, in the order they matter:

- *It does not reduce the fan-out.* The six sites stay six, with `is
  SequencerNodeDef` where `is not null` was. Inheritance pays only when consumers
  call a virtual instead of testing the type, and most of these cannot: the
  inspector needs Avalonia, which the engine does not reference.
- *It puts `NodeDef`'s constructor in the plugin ABI.* A derived record must call
  the base positional constructor, so adding a parameter would break every
  derived definition in every plugin at load rather than at recompile. The init
  properties on `NodeDef` and `EmitContext` exist precisely to avoid that
  ([0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)), and a hierarchy
  would give it straight back.
- *The kinds are independent axes.* A module may want notes and a scale both, and
  single inheritance would have to name every combination of three. That a
  modular synth would usually patch two modules together rather than combine them
  makes this the weakest of the three reasons, and it is about combining the
  kinds that exist — neither shape lets a plugin add a *new* one, for the reason
  below.

**Four things live on the part**, and they are the four that are both in the
engine and true of every kind. `Seed` says what a fresh instance carries.
`Fold` reads the state onto the `EmitContext`, tidying on the way. `Report` says
what one instance holds, and `Announce` says that the module holds it at all —
both for the assistant, which would otherwise see neither, since this is neither
a socket nor a wire.

**Copying is not one of them.** It is `NodeInstance.Clone`, next to the fields it
copies. A copy must work on a module this build has no definition for — a
fragment naming a plugin that is not loaded still has to keep its notes — and
looking a definition up to find nothing would drop them silently. This is the one
place that names every field an instance carries, and it is in the file where
they are declared.

**Editing is not one of them either.** The engine does not reference Avalonia, so
the App maps an extra to a control. A kind with no editor there shows nothing
rather than throwing.

**`StepSpec` gathers the four step members into one.** The notes, how a value
reads, and the range it is edited within only ever mean anything together. Held
that way, a range on a module with no notes cannot be written down.

## Consequences

**Adding a fourth kind costs a file, one line on the definition, one `switch`
arm in the App, and one property on `EmitContext`.** It was six edits across five
projects.

**`EmitContext` keeps its typed properties.** `Steps`, `Scale`, `Sample` and
`Trace` stay what they are, and `Fold` sets them. That type is the *read* side:
`node.Sample is not { } clip` inside an emit function is the feature, and a
generic bag addressed by key would be worse for the one audience that matters
most.

**`NodeInstance` keeps its typed fields, so the file format did not move.** A
patch is serialised straight from the object graph, and a dictionary of extras
would have changed every saved file and made none of them readable by hand.

**A normalled module now carries its extras.** `PatchCompiler.Hidden` built its
context from the definition's defaults directly, which meant a socket normalled
to a module that reads a file got no clip at all — nothing there went looking for
one. It now seeds a scratch instance the way a placed module is seeded and folds
that, so a hidden module carries what a placed one would. Nothing in the built-in
catalogue was normalled to a module with extras, so this fixed a hole rather than
a symptom.

**The compiler no longer holds one module's error prose.** The two complaints
about a missing sound file were twenty lines inside `PatchCompiler`; they are on
`SampleExtra` now, reached through an `ExtraEnv` that lends an extra the three
things a node cannot tell it — what it is called, where a file is found, and
where a complaint goes.

**`Extra<T>()` makes "does this module carry notes" a question.** It replaces
seven `DefaultSteps is not null` tests, and the inspector's "nothing to set" line
now reads `Inputs.Count == 0 && Extras.Count == 0` — which is also stricter than
what it replaced, since that one had never counted a file.

**The kinds are open to derive from and closed to use, and that is a trap worth
naming.** `NodeExtra` is public and abstract, so a plugin may derive one and the
loops will call it — but `Seed` and `Fold` have nowhere to work. `NodeInstance`
is sealed and its fields are `Steps`, `Scale` and `Sample`; `EmitContext` is a
readonly record struct with the matching four properties; and `PatchIo`
serialises the declared properties of `NodeInstance` and nothing else. So a
plugin's own kind can *describe* state it cannot *hold*: `Announce` and `Report`
work, the rest silently does nothing. The one honest escape is to reuse an
existing field — an extra that stores in `Steps` and folds into `ctx.Steps` works
end to end, and simply gets no editor.

Opening it would mean a keyed store on `NodeInstance` and another on
`EmitContext`, which is an additive change to the file format and a loss of typed
access at the one place typed access is worth most — inside an emit function.
That is not paid for by a plugin that has not asked yet. Until one does, this is
a limitation rather than a design: if a plugin does ask, the store is the answer,
and sealing the hierarchy in the meantime would turn a silent no-op into a
compile error at the cost of saying the question is settled.

**The wariness in 0051 still applies, and is now cheaper to ignore.** Making the
fourth kind easy to add is not an argument that there should be one. The test is
unchanged and it is the test that matters; what this record changes is only what
passing it costs.
