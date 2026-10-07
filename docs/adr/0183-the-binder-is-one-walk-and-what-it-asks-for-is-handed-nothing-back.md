# ADR-0183: The binder is one walk, and what it asks for is handed nothing back

**Status:** Accepted · 2026-10-07 · *user-directed* · implemented in
`Language/Binder.cs`, `Wiring.cs`, `Formulas.cs`, `Issues.cs`, `SourceSites.cs`,
`Carrying.cs`, `PatchLines.cs`, `PanelDeclaration.cs`, `Boxes.cs`,
`PipeLanding.cs` and `Language/Values/` · rests on
[0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0106](0106-a-sum-in-the-text-is-one-expression.md)

## Context

`Binder` had grown to nineteen hundred lines. A cut made in September took the
walk over expressions out into `ExpressionBinder` and `ArithmeticBinder`, and
those two took nineteen delegates back into `Binder` to reach what they needed:
`Bind`, `Call`, `Pipe`, `Place`, `Complain` and the rest. A later seam took the
seven fields that record where the text says things out into `SourceSites`, and
proposed the next pieces the same way, each taking "three objects instead of a
dozen callbacks".

The callbacks are not an accident of how the cut was made. Binding is one mutual
recursion: an expression holds a call, a call binds its arguments, an argument may
be a sum or a pipe, a pipe lands on a call, a def's body is run with the same
`Run` that ran the statement calling it. Any piece cut out of that recursion
needs the rest of it, and a delegate is the only way back. Cutting along "which
fields" keeps producing bags of delegates because the fields are not the seam.

## Decision

**`Binder` holds the walk, and only the walk.** `Build`, the statements, the
names and what they are worth, the expressions, the sums, the pipe rule, calls
and defs. Nothing that recurses into binding lives anywhere else.

**Everything the walk asks for that never needs to ask back is a class of its
own, handed nothing from `Binder`.** It takes the catalog, the issues, the sites
or the wiring, and values; never a delegate:

- `Wiring`: the patch under construction. Placing a module, running a wire,
  setting a knob and linking a panel knob, with the refusals for a socket wired
  or set twice, and the one clock and one Coordinates a bare word reaches for.
- `Formulas`: what a sum knows on its own. Folding two numbers, the signals a
  term reads, the formula text and the brackets it needs. Static, since it holds
  nothing. The loop that splits a sum too wide for one Expression stays in the
  walk, because it places as it settles, and the order it places in is the
  order the modules are named in ([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)).
- `Issues`: the complaints, said the same way by every piece.
- `SourceSites`: where the text says each thing, handed over as the `SourceMap`.
- `Carrying`, `PatchLines`, `PanelDeclaration`, `Boxes`: the readers for what a
  call gives a module to carry, the lines about the whole patch, a panel line's
  settings, and the groups' boxes.
- `PipeLanding`: where a pipe lands when the text does not say. Static, once a
  `Socket` carries its `NodeDef` like a `Placed` does, so a value answers its
  own width, part and kind.
- `Values/`: what a name can stand for, one record per file.

**A piece that would need a delegate back into `Binder` is a piece of the walk,
and stays.** A reader that needs the scope, or has to bind something, is not a
reader; the checks around `PanelDeclaration.Read` are in `Binder.Declare` for
that reason.

## Consequences

- `Binder` is about fourteen hundred lines, and every one of them is the
  recursion. A new statement kind is a case in `Ran` and, where it needs nothing
  of the walk, a reader beside `PatchLines`.
- Not one module is named differently: the walk places in the order it did, and
  the suites that compare printed text, source maps and rebuilt patches say so.
- Declined on the way: one argument plan shared by a module call and a def call.
  They look alike, named arguments first and positionals filling what is free,
  but the rules differ by design: a def takes `_` positionally and a module does
  not, the landing rules differ, a def has defaults and a module has fields,
  paths and ranges. Only the two small loops are common, and sharing them would
  cost hooks for every difference.
