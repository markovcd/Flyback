# ADR-0038: A sequencer's notes are a list on the node

**Status:** Accepted · 2026-08-17 · *user-directed* · supersedes the central
decision of [0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md); amends
[0009](0009-editable-defaults-on-every-input.md) and
[0020](0020-json-patch-files-keyed-by-string-type-ids.md)

## Context

[0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md) made each step a pair
of ordinary inputs, and was explicit about why: a module is a fixed `NodeDef`, an
instance is a `float[]` of knob values, a file is those two things in JSON, and
"a pattern of arbitrary length fits none of them without a new kind of storage
running the whole width of the program — a port kind, a serialiser, an inspector
control and a compiler path."

That was the right answer for eight fixed steps. It bought a real capability —
an oscillator patched into step 3 makes one note drift while the other seven
hold — and it cost the ability to have any other number of them. The cost has
since become the thing you notice:

- **More than eight** meant chaining two sequencers through a bank selector and
  a Mix: three modules and ~216 ops to express one tune.
- **Fewer than eight** meant a `steps` knob that shortened the pattern while
  leaving the unused knobs on the node.
- **Uneven notes** were not expressible at all. Every step was one step long,
  so a sequence could have rhythm only by spending notes on it — a half-note is
  the same note twice with the gate held open, which the gate is specifically
  built to prevent.

And sixteen steps as inputs would have been thirty-two sockets on a node already
514px tall; thirty-two would have been ~1474px.

## Decision

**The notes are a list of data on the instance, and the module has four inputs.**

```csharp
public readonly record struct Step(float Value, float Length = 1f, float Volume = 1f);
```

`NodeInstance.Steps` is a `List<Step>?`, null for every module but the two
sequencers, serialised by `System.Text.Json` with no work in `PatchIo`. The
module declares `in`, `rate`, `gate length` and `shape` — the tune is not among
them.

Notes can be inserted at any position, removed, and reordered, because a list
can be and a row of sockets cannot. Up to 32, which is a budget rather than a
structure: see the costs below.

**The emit reads the notes, so `EmitFn` carries them.**

```csharp
public readonly record struct EmitContext(Slot[] Inputs, IReadOnlyList<Step> Steps)
{
    public Slot this[int port] => Inputs[port];
}

public delegate Slot[] EmitFn(Emitter emitter, EmitContext node);
```

The indexer is the whole reason this was cheap: every one of the 41 emit lambdas
in the catalogue and all four in the plugins index their inputs and never treat
them as an array, so all of them compiled **unchanged**. Only the delegate, the
single call site in `PatchCompiler`, and one test harness moved.

The alternative — appending the notes to `inputs` as constant slots — was
rejected. It needs a way to read a constant back out of a register that does not
otherwise exist, and it makes `inputs.Length` disagree with `Inputs.Count`, which
is a lie about the one thing that delegate's contract states.

**Lengths fold at compile time.** The notes are values, not registers, so where
each note starts is a running sum the compiler works out in C#. Selection is
still a sum of windows and still has no branches; the windows compare against
those sums instead of against integers.

**Even patterns take the route they always took.** When every note is the same
length the sequence can be counted in whole notes, so the edges fall on integers,
how far through a note we are is a plain `Fract`, and which note is playing is
one division. That is exactly the op sequence 0031 emitted. Only an uneven
pattern pays for its unevenness, and it pays by selecting each note's start and
length the same way it selects the value.

## Consequences

**A step is no longer a socket, and that capability is gone.** 0031's
"patch an oscillator into step 3 and that note drifts while the other seven
hold" cannot be expressed any more. This was given up knowingly. It is the one
thing this record takes away and nothing here gives it back — a list that grows
and shrinks cannot have a fixed port per entry, and the two cannot both be true.

**What it costs, measured.** Ops for the whole module including its four input
constants:

| notes | even | uneven |
|---|---|---|
| 1 | 24 | 26 |
| 4 | 46 | 69 |
| 8 | **78** | 129 |
| 16 | 142 | 249 |
| 32 | 270 | 488 |

Eight even notes is what eight steps cost before, to within the two ops that the
`steps` knob's clamp-and-floor used to take — the Sequence preset's shader went
from 111 ops to 109. A four-note pattern now costs a little over half what it
used to, because it used to cost eight notes' worth whatever the knob said.

Thirty-two uneven notes is 488 ops, six times Nebula, evaluated per pixel on the
video path. That is the real limit, and it is why 32 is the cap. It also makes
0031's still-open note about hoisting per-frame invariants out of the pixel loop
worth more than it was: a sequencer driven by `t` does identical work at every
pixel of the frame.

**0031's refusal is now paid for.** All four things it listed have arrived: a
type (`Step`), a serialiser (automatic, but the JSON is wider), an inspector
control (`StepList`), and a compiler path (`EmitContext`). The judgement that
has changed is not about the cost — it is that a sequencer whose length cannot
change is not worth the saving.

It is deliberately the narrow version. `Steps` is one typed list used by one
module family, not an untyped blob any module might fill. The warning 0031 wrote
still applies to the general case, and the general case is still refused.

**Every saved patch containing a sequencer breaks**, and this is the second
format break in a day after
[0037](0037-one-output-block-that-every-patch-has.md). Both were taken
deliberately with no migration, on a program whose saved patches are few and
whose presets are rebuilt from code. It should not become a habit.

**The assistant needed a new verb.** Notes are neither wiring nor knobs, so
`set_knobs` could not reach them and `describe_patch` would have shown a
sequencer as a module with nothing set on it. `set_steps` replaces the whole tune
in one call rather than offering add/remove/move separately: a model that
rewrites eight notes at once cannot get them into the wrong order, and eight
calls that each have to land correctly is eight chances not to.

**`PortDisplay.Count` was removed.** It was added hours earlier so the `steps`
knob would stop showing 7.35 while seven steps played, and that port no longer
exists. What survives is `PortSpec.Stepped`, which the list editor uses to make a
note land on notes — the same rule, now applied to a list cell instead of a knob.

**The list editor is composed, not drawn.** That is the opposite call to
[0017](0017-draw-the-node-editor-in-one-control.md), on purpose: that record
draws because it zooms and because a wire must end where a socket was painted,
and neither is true of a panel. Drawing this one would have meant hand-rolling
text entry and giving up the keyboard, which are the costs 0017 accepted for the
canvas and has no reason to accept here.

**The gate is derived differently and needed pinning.** It is shaped against how
far through *this* note we are, so a two-step note opens and closes over its own
length rather than sounding for half of itself and resting. 0031 built that
envelope specifically to stop a stepped amplitude clicking, and the only thing in
the repository that can detect the failure is its own measurement — largest
sample-to-sample jump against the wave's own travel. That measurement is now
pointed at an uneven pattern as well as an even one.
