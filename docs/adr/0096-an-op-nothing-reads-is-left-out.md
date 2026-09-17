# ADR-0096: An op nothing reads is left out

**Status:** Accepted · 2026-09-18 · *user-directed* · amends
[0011](0011-compile-backwards-from-output.md)

## Context

[0011](0011-compile-backwards-from-output.md) made dead-code elimination a
property of the walk: what the Output cannot reach is never visited, so it emits
nothing. That is true of modules and stops at them. A module that is reached is
emitted whole, because its emit function runs once, returns every output, and
has no idea which of them a wire takes.

For most modules the difference is an op or two. For some it is most of the
module. Random works out white, pink, a stepped value and a drifting one, and
pink is thirteen of its sixteen noise lookups — so a patch that wanted hiss for a
hat paid for thirteen rows of pink on every sample at four times the sample
rate. It measured at about seven per cent of real time, and the showcase presets
answered by not using it: six of the eight build white noise out of a Sine and
a Fraction instead. Coordinates measures a radius and an angle for every patch
that reads x. A sequencer works out a gate, an index and a volume for a patch
that reads the value.

The shader never paid for this, since a GLSL compiler throws away what nothing
reads. The processor did: both the interpreter and the IL
([0076](0076-the-processor-runs-a-program-as-il-once-it-is-built.md)) write every
op's result to a bank of registers, which no JIT may treat as unobserved.

Two ways out. A module could be told which outputs are wired and skip the rest —
small, and it helps only the modules somebody remembers to change, one at a
time, for ever. Or the program could be swept once it is whole.

## Decision

`Emitter.ToProgram(result)` leaves out every op nothing reads, and the compiler
builds its program with it.

One pass backwards over the op list. A register is read if it is part of the
result, or an input of an op that is kept; an op is kept if any register it
writes is read. The program is SSA and in emit order
([0005](0005-compile-to-a-flat-register-machine.md)), so by the time the pass
reaches an op everything that could read it has already been seen, and one pass
is the whole analysis.

Two kinds of op are kept whatever reads them, and `OpShape.Kept` is where that
is said:

- **An op that writes no register** — `Tap`, `UnitWrite`, `PlaneWrite`,
  `ClockWrite`. A register was never what it was for. This is by construction
  rather than by list: an opcode added later that writes nothing is kept without
  anybody remembering to say so.
- **`Delay`, `Allpass` and `Phase`.** Their memory is theirs by position: a
  renderer hands out delay lines and phase cells in the order these ops run, and
  `StateOwners` ([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md))
  names their modules in that same order. Taking one out would hand every one
  after it its neighbor's past.

Registers keep their numbers. The bank has holes in it afterwards, and every
slot anything outside the op list holds — the result, a trace, a test — means
what it meant.

`ToProgram()` with no argument still returns everything emitted, for a program
assembled by hand.

## Consequences

An output no wire takes costs nothing, in every module, including ones in
plugins that were written before this and will never hear of it. Random's white
is one lookup. The sound programs of the showcase presets that make a sound
lose between two and nineteen per cent of their ops — Bronze goes from 1,848 to
1,488, Outrun from 2,149 to 1,960 — and the picture programs between six and
twenty-three.

It is a pass, which [0011](0011-compile-backwards-from-output.md) said
dead-code elimination was not. It is the same rule one level down, and it keeps
the property [0021](0021-recompile-the-whole-patch-on-every-edit.md) cared about:
it is re-derived from the ops on every compile and caches nothing, so it cannot
go stale.

The shaders get shorter, and their constants renumber, since a literal only a
dropped op read is dropped with it. Nothing about what they draw changes.

What is conservative stays paid for. An unread `Delay` still runs, and so does
whatever feeds it; a cell written and never read is still written. Both are
rare — a stateful module's outputs nearly always share the memory, so reading one
keeps all of it — and lifting either means renumbering memory and its owners
together, which is not worth it for what is there to win.

A test that counts ops now counts the ones that matter. Three did otherwise and
said so in their comments — "which emits all five of its outputs whether or not
a wire takes them" — and now pin what they were about.
