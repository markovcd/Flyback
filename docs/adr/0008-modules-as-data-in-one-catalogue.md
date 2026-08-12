# ADR-0008: Modules are data in a single catalogue

**Status:** Accepted · 2026-08-11 · amended by
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md), where the
catalogue is composed from the engine's modules plus any a plugin adds

## Context

The synth ships 52 modules. Each one needs to: appear in the palette under a
category, declare its input and output sockets with names and defaults, draw at
the right height with the right number of port dots, offer a description as a
tooltip and in the inspector, round-trip through JSON, and lower to ops.

The reflexive C# answer is a class hierarchy — `abstract class Node` with
`abstract Slot[] Emit(...)`, one subclass per module. That means 52 files, and
every one of those seven concerns becomes either an abstract member or an
attribute.

## Decision

A module is a record, and all of them live in one list:

```csharp
public sealed record NodeDef(
    string TypeId, string Name, string Category,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    EmitFn Emit,
    string Description = "");
```

`EmitFn` is `delegate Slot[] EmitFn(Emitter emitter, Slot[] inputs)` — behaviour
as a delegate field rather than an overridden method. `NodeCatalog.All` is a
collection expression of these, indexed by `TypeId` in a static constructor.

## Consequences

Adding a module is one entry in one file. Nothing else changes: it appears in
the palette because the palette enumerates `NodeCatalog.Categories`, it draws
because `NodeGeometry` measures from `def.Inputs.Count + def.Outputs.Count`, it
compiles because `PatchCompiler` calls `def.Emit`, and it serialises because only
its `TypeId` is stored.

Families collapse into helpers. Ten binary maths modules are ten lines:

```csharp
Binary("math.add", "Add", OpCode.Add, 0f, "a + b"),
Binary("math.mul", "Multiply", OpCode.Mul, 1f, "a * b"),
```

and the five oscillators share one `Oscillator(...)` factory that differs only in
the waveform lambda applied to the running phase. Under a class hierarchy those
would be fifteen files of mostly identical boilerplate.

The whole instrument is readable in one sitting. `NodeCatalog.cs` is 301 lines
and is the definitive answer to "what can this thing do".

The trade-off is that a module cannot carry per-instance state or custom
behaviour beyond its emit function. Nothing currently wants to — the graph holds
all instance state, in `NodeInstance.InputValues` — but a module needing, say, a
file reference or a lookup table would not fit this shape without extending
`NodeDef`.

There is also no compile-time link between a module's declared port count and
what its `EmitFn` indexes. `i[3]` on a three-input module throws at compile time
rather than failing to build. With every definition adjacent to its emit lambda,
that mismatch is visible in the same expression, which is the mitigation.
