# ADR-0242: A pack's module is one module, expanded before it is compiled

**Status:** Accepted · 2026-11-26 · *user-directed* · follows
[0241](0241-a-module-pack-is-a-text-that-offers-modules-by-id.md)

## Context

A pack offers `module` defs by id. Placed in a patch, one could arrive as a def call does
today, the modules it is made of in a shut box, or as one module the way a plugin's does.
The user chose one module: the modules being moved out of plugins keep their look, their
knobs stay on the module, and a saved patch holds one instance rather than its insides.

The compiler lowers each module through its `EmitFn`. A pack's module has none of its
own: its body is text, built from modules that do.

## Decision

**A pack's module is one `NodeDef` in the catalog**, its type id `<pack id>.<name>`, its
inputs the parameters with their defaults, its outputs what its body ends with (`out`, or
`out_1`, `out_2` for a tuple), and its name, category, help and words from its card.
`PackModules.Into` adds a pack's modules to a catalog under the pack's id as their
provider, as a plugin's are; a pack with any mistake in it adds none.

**Each module is built once, around stand-ins**, when its pack is added: the pack's text
and a call to the module whose arguments are stand-in sockets and whose results go into
stand-in sinks (`flyback.pack.socket`, `flyback.pack.result`, in a provider only the
stencil's build sees). What the results reach back to is the module's stencil. Building
it is what checks the module's body, so a mistake in it is said on the pack's own line.

**The compiler never sees one.** `PatchCompiler` expands a patch before walking it
(`PackExpansion`): each placed pack module is swapped for a copy of its stencil, a wire
into a socket runs to wherever the body reads it, an unwired socket's knob is written
into those places, and the wires out of each output leave from what its body ended with.
A copy is made only where the patch holds a pack module, and the patch itself is never
changed. A module inside has an id derived from the placed module's and its own, so
oscillator phases and delay lines carry over from one compile to the next. A module
switched off hands its `in` on, as any module does.

## Consequences

- A pack's module costs exactly the modules its body is made of, on every backend.
- The editor, the inspector and saved patches see one module; only compiling looks inside.
- Not yet covered: a panel knob or MIDI linked to a pack module's socket, a probe or a
  meter on the module itself, and a bundle carrying the packs its patch uses. They are the
  next steps in `docs/handoff/module-packs.md`.
