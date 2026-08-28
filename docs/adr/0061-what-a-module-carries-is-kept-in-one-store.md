# ADR-0061: What a module carries is kept in one store

**Status:** Accepted · 2026-08-28 · *user-directed* · finishes
[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) and closes the
limitation [0055](0055-a-plugins-extra-declares-its-editor.md) left open

## Context

[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) made a kind of
carried state a part of a definition — one `NodeExtra` per kind, iterated rather
than enumerated — and named its own trap while doing it: "the kinds are open to
derive from and closed to use". `NodeExtra` was public and abstract, but
`NodeInstance` was sealed with `Steps`, `Scale` and `Sample` on it, so a plugin's
own kind could *describe* state it could not *hold*.
[0055](0055-a-plugins-extra-declares-its-editor.md) opened it, exactly as 0054
said it would have to: a keyed store, `Dictionary<string, JsonNode>? State`, with
a plugin's extra writing under its `Key`.

That left the store and the typed fields side by side, doing the same job for
different authors. 0054 declined to move the engine's own kinds into it for one
reason and one only, stated plainly: "`NodeInstance` keeps its typed fields, so
the file format did not move." That was the whole argument. The other cost it
named — losing typed access "at the one place typed access is worth most, inside
an emit function" — turns out not to be a cost at all, because emit functions do
not read a `NodeInstance`. They read an `EmitContext`, and what puts a value on
one is `Fold`.

What the split cost was paid by `NodeInstance.Clone`. 0054 put copying there
rather than on the part, correctly: a copy must work on a module this build has
no definition for, and looking one up to find nothing would drop a fragment's
notes without a word. But that made `Clone` the one place naming every field an
instance could carry, and a field added without a line there is the mistake that
goes wrong quietly — a paste that silently forgets something.

## Decision

**Everything a module carries that is not a knob lives in `NodeInstance.State`,
under the extra's `Key`.** `Steps`, `Scale`, `Sample` and `Picture` are gone as
fields. `StepsExtra`, `ScaleExtra`, `SampleExtra` and `PictureExtra` seed, read
and fold through the store like a plugin's kind does.

**A kind owns the shape it keeps there, and hands it back typed.**
`StepsExtra.Of(node)` returns a `List<Step>`, `ScaleExtra.Of(node)` a list of
pitch classes, `SampleExtra.Of(node)` a path. Static rather than on the instance,
because a preset builder and the inspector both hold a node without holding a
definition. `Set` is the other half, and it replaces outright — a tune and a
scale were always edited whole.

**What is uniform is the storage, not the shape.** `ExtraField` describes a
number, a toggle and a choice; a tune is none of those and neither is a path. So
the engine's four go on overriding `Seed`, `Fold` and `Report` rather than
declaring fields, and the App goes on mapping them to controls written for them.
Moving the storage was never going to make the *editors* uniform, and pretending
otherwise would have meant inventing `ExtraField` kinds nobody asked for.

**The emit path did not move.** `EmitContext` keeps `Steps`, `Scale`, `Sample`
and `Picture` as typed properties and `Fold` still sets them, so every emit
function in the catalogue reads exactly what it read before. This is what makes
the change cheap: the storage and the reading were already separated by `Fold`,
and only the storage side of it moved.

**Tolerance moved with it.** A hand-edited file used to reach a `List<Step>` the
deserialiser had to satisfy, so a note missing its value threw out of the middle
of loading a patch. It now reaches an opaque tree that `Of` reads and gives up
on, answering the empty tune. That is the contract `Step.Sane` already had, one
level further out.

**The file format moved and no upgrade step was written.** `"Steps": [...]` is
now `"State": { "notes": [...] }`. `PatchIo.FormatVersion` stays at 1 and
`Upgrade` stays empty: patches saved by earlier builds lose whatever they
carried. Accepted deliberately by the project owner rather than overlooked —
there is no released build and no corpus to protect, and the alternative was
`[JsonExtensionData]` and a 1→2 step existing only to serve files nobody has.
**This is the one thing here that cannot be un-decided later**, and the reason it
is recorded rather than merely done.

## Consequences

**`Clone` stops naming fields.** Identity, position, name, `InputValues` and one
deep clone of the store. The reason 0054 kept it off the part is now better
served rather than merely preserved: a fragment naming a plugin that is not
loaded keeps what it carries because nothing in the copy has to know what any of
it is. A kind added later is copied without this being touched — which is the
class of mistake that was available before and is not now.

**The trap 0054 named is gone rather than worked around.** There is no engine
half and plugin half; a kind derived anywhere seeds, folds and copies through one
path. `NodeInstance` is down to what every module has — identity, place, name,
knobs — plus one store that is null on nearly all of them.

**The patch file is arguably better to read.** A module carrying nothing writes
no `State` at all, and one that carries something writes it under a word: `notes`,
`scale`, `file`, `picture`. The nesting is one level deeper than it was and the
name is the extra's own, so what a key means is answerable by finding the kind
that owns it.

**The step editor keeps a working copy.** `StepList` inserted into, removed from
and reordered a live `List<Step>` on the node; `Of` hands back a fresh list each
call, so it now holds one and `Save`s at every point it tells the patch something
changed. That is one more thing to keep true, and it is the only place in the
program where it is true.

**A test that mutated what it read no longer proves anything, and had to say what
it meant instead.** `pasted.Scale!.Clear()` was a real check that a paste is a
deep copy; against a store it edits a copy and passes for the wrong reason. It
reaches through `StateOf` and clears the array now, which is what it was always
testing. Three others were the same shape. This is the sharp edge of the change:
`Of` returning a copy is safe everywhere except where a test was relying on it
not being one, and nothing warns about it.

**A module carrying nothing under its key is still askable.** `Of` answers empty
for "carries none" and for "carries an empty one" alike, so the two are told
apart with `StateOf(Key) is null` where it matters — which is a question about
the file rather than about the module, and only tests have had to ask it.
