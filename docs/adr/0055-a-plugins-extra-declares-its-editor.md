# ADR-0055: A plugin's extra declares its editor

**Status:** Accepted · 2026-08-26 · *user-directed* · opens what
[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) recorded as closed,
and extends the storage
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) describes

## Context

0054 made a kind of carried state a part rather than a member, and recorded the
limit it left standing: `NodeExtra` was public and derivable, but `NodeInstance`
is sealed with three typed fields and `EmitContext` has the matching four, so a
plugin's own kind could `Announce` and `Report` state it had no way to hold. That
is the worst shape an extension point can have — it compiles, it runs, and it
silently does nothing.

Closing it needs two things: somewhere to put the state, and something to edit it
with. The first is a keyed store and is uncontroversial. The second is the
decision worth recording.

**The obvious answer was to let a plugin ship a control.** A second assembly
referencing Avalonia, loaded by the shell and not by the CLI, registered
alongside the modules. It fits the host: `IPluginRegistry` says "new kinds of
extension are new methods here", and `PluginLoadContext` already loads a folder's
assemblies lazily into a context of its own.

It founders on type identity. That context shares exactly two assemblies with the
host — `Flyback.Core` and `Flyback.Plugins` — and resolves everything else
privately, so a plugin's Avalonia would be a *second* Avalonia and the `Control`
it returned could not go in the shell's visual tree. The file naming those two
says what happens then: "the same type would have two identities and every cast
across the boundary would fail". There is no way round it while a plugin
constructs objects the host displays. Avalonia would have to become host-owned,
and that is a much heavier promise than the two already there: those are versioned
by this repository, and Avalonia is a third-party package on its own cadence.
Every plugin UI binary would be pinned to the exact version a given build
shipped, and the pair held out of the single-file bundle would grow to include
most of the shell's bulk.

## Decision

**A plugin declares what it carries; the App draws it.** `NodeExtra.Fields`
returns a list of `ExtraField`, and no plugin ships a control. Avalonia stays out
of `PluginLoadContext.HostOwned`, no plugin binary is pinned to a version of it,
and the CLI needs no rule about which assemblies to skip — there is no UI
assembly to skip.

**The vocabulary is two shapes, and stays that way until a plugin is blocked.**
`Number` and `Toggle`. Every shape here is public API that cannot be withdrawn,
and the pressure will be to guess at a choice, a path and a list of records
before anything needs them.

**A number field is a `PortSpec`.** Not a range of its own: that record already
means "a number with a range, a display and whether it snaps", the App already
draws one, and `PortDisplay` already writes 57 as "A3" and -3 as "1 ms". The
control was extracted so a socket's knob and a declared field are literally the
same code, which is why a plugin's row reads like the knob two rows above it.

**Storage is a keyed store beside the typed fields, not instead of them.**
`NodeInstance.State` holds a `JsonNode` per extra key; `Steps`, `Scale` and
`Sample` stay exactly where they were. So a patch with no plugin extras in it is
byte-for-byte the file it always was, and one with them is still ordinary
hand-editable JSON.

**`EmitContext.Extras` carries the folded values as `object`, read through
`Extra<T>`.** The engine does not know the shape and does not need to: a plugin's
`Fold` puts it there and that same plugin's emit function takes it out. What the
emit function sees is `ExtraState`, already parsed — the JSON never reaches it.

**Everything below `Fields` has a default written in terms of it.** `Seed`,
`Fold`, `Report` and `Announce` became virtual. A plugin's extra overrides `Key`
and `Fields` and nothing else; the engine's three override all of them and
declare no fields.

**The assistant gets one `set_extra` for every kind a plugin will ever add.**
This is the return that shipping a control would not have paid: the three
built-in kinds each needed a tool written for them, and no plugin's will.
Per-field rather than whole-object, which is the opposite call to `set_steps` and
made for the opposite reason — a tune's order is the point, so half of one
applied is a tune nobody asked for; these are named values that do not depend on
each other, and setting one is as safe as setting a knob.

## Consequences

**A field's range is what the value means; a socket's is a suggestion.** A saved
knob outside its port's range widens the slider, deliberately. A field is held to
its range on every path in — the inspector, `set_extra`, and the fold before a
compile. Two rows that look identical therefore behave differently at the edges,
which is worth knowing; the alternative was for a declared range to be advice,
and then nothing could rely on it.

**What a plugin cannot have is a control of its own.** No keyboard, no waveform,
no list you reorder. The engine's own three kinds are the honest measure of that
cost: all three needed a bespoke control, and under this rule none of them could
have been written by a plugin. The wager is that a plugin's state is usually a
few numbers and switches, and that the ones which are not are better served by
adding a shape here — where every plugin gets it — than by opening the door to
arbitrary UI.

**The escape hatch is still open and now costs more to take.** If a plugin ever
genuinely needs its own control, `IExtraEditor` in a host-owned contract assembly
is still the answer, and nothing here has to be undone for it. But the Avalonia
version lock is the price then as now, and the bar should be a plugin that is
actually blocked.

**A plugin can still reuse a built-in field.** An extra that stores in `Steps`
and folds into `ctx.Steps` works end to end and always did. It gets no editor,
because `EditorFor` matches the engine's three by type. That is a sharp edge
rather than a feature.

**The file format did not move.** `State` is one more optional property that an
older reader ignores, which is the same rule that says adding a module does not
raise `FormatVersion`.

**`NodeExtra.Key` is load-bearing now.** 0054 removed it as dead. It is what a
plugin's state is filed under in the store and in the folded context, so it is in
every saved patch that holds the module and cannot be changed without changing
that file.
