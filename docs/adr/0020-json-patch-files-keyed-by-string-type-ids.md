# ADR-0020: JSON patch files keyed by string type IDs

**Status:** Accepted · 2026-08-11 · amended by
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md), which adds a
header naming the plugins a patch cannot be opened without

## Context

Patches need saving. A patch is a small object graph — a list of node instances
and a list of connections — with no cycles once wires are stored as ID pairs
rather than object references.

Two questions: what format, and how does a saved node identify its module type?

The second matters more. If a node stores a numeric index into the catalogue,
inserting a module at position 3 silently reinterprets every saved patch.

## Decision

`System.Text.Json`, indented, with no custom converters. Extension `.fbk`.

Nodes store their module's **string** `TypeId` (`"osc.sine"`, `"space.kaleidoscope"`),
never an index or a .NET type name. Connections store node `Guid`s plus port
indices. Knob values are a positional `float[]`.

## Consequences

The format is human-readable and diffable, which for a creative tool means
patches can be shared as text and inspected when something looks wrong.

Serialisation needed no code. `Patch`, `NodeInstance` and `Connection` are plain
types with public members, and .NET 10's `System.Text.Json` handles `required`
and `init` members and record positional constructors natively
([0001](0001-target-net-10.md)) — `PatchIo` is 18 lines and all of it is
convenience wrappers.

String type IDs decouple the file format from both catalogue order and C#
identifiers. Modules can be reordered, renamed in the UI (`Name` is separate from
`TypeId`), or moved between categories without touching saved patches. Renaming a
`TypeId` is the one breaking change, and it is a visible one.

That has happened exactly once. The video sink was `output`, unqualified because
it predated sound, and became `video.output` to match `audio.output`
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)). It cost nothing
because no patch had yet been saved anywhere — which is the only condition under
which a `TypeId` rename is free. A later one needs a load-time alias table, not a
rename.

Unknown modules degrade rather than fail. `PatchCompiler` reports
`"Unknown module 'x'"` as a compile issue and emits a constant zero
([0011](0011-compile-backwards-from-output.md)), so a patch using a module this
build lacks still opens and still shows everything else.

Port count changes are partially handled. `DefaultFor` falls back to the
definition's default when `InputValues` is shorter than the current input list,
so *appending* a port to a module keeps old patches working. **Reordering or
removing ports does not**, because values are positional — they would be silently
reassigned to the wrong knobs. Keying values by port name would fix this and was
not done; appending is the only catalogue change currently safe for saved files.

There is no schema version field. Adding one costs a property and would give
future migrations somewhere to hook; its absence is the main thing worth
revisiting before anyone else's patches exist.

Node canvas positions are stored, so a patch reopens laid out as it was left.
