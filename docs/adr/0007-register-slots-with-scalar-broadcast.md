# ADR-0007: Values are register slots; scalars broadcast to colours

**Status:** Accepted · 2026-08-11

## Context

Two kinds of value travel down wires: a scalar signal, and a colour. The
register machine ([0005](0005-compile-to-a-flat-register-machine.md)) stores
only `float`s, so colours need a representation, and the maths modules need to
work on both without being written twice.

Shading languages solve this with vector types and operator overloading:
`vec3 * float` is defined, and scalars broadcast. Something equivalent was needed
without introducing a second value type into the register file.

Three options:

1. **Make everything three-wide.** Every value is RGB; scalars are triples of
   equal components. Uniform and simple — and triples the op count for the
   majority of the graph, which is scalar.
2. **A tagged value type in the register file.** Registers hold a struct that is
   either a float or a float3. Adds a branch to every op and inflates the file.
3. **Track width at compile time; keep the register file flat.**

## Decision

Option 3. A compiled port value is a `Slot`:

```csharp
readonly record struct Slot(int Base, int Width)   // Width is 1 or 3
{
    public int Component(int i) => Width == 1 ? Base : Base + i;
}
```

A colour occupies three *consecutive* registers. `Component(i)` is where
broadcasting happens: a width-1 slot returns the same register for every
component, so a scalar read three times is a scalar applied to all channels.

Component-wise ops are expanded at emit time. `Emitter.Binary` takes the wider
of its two operands and emits that many scalar ops:

```csharp
var width = Math.Max(a.Width, b.Width);
for (var i = 0; i < width; i++)
    Add(new Op(code, first + i, a.Component(i), b.Component(i)));
```

## Consequences

The interpreter never learns that colours exist. It executes scalar ops on a
flat `float[]`, and `OpCode` needs no vector variants — which is most of why
`Evaluate` stays a readable switch ([0006](0006-scalar-interpreter-parallel-over-rows.md)).

Scalar work costs scalar ops. A patch that is mostly coordinate maths with a
colour conversion at the end pays for three-wide arithmetic only at the end,
which is the common shape.

Multiplying a colour by a scalar works with no special case, and that is what
makes `Any` ports viable ([0010](0010-any-typed-ports-for-polymorphic-maths.md)):
one `Multiply` module handles `colour * scalar`, `scalar * scalar` and
`colour * colour` because `Binary` resolves widths itself.

Contiguity is load-bearing. `colour.split` returns `Slot.Scalar(base)`,
`Slot.Scalar(base + 1)`, `Slot.Scalar(base + 2)` — it compiles to no ops at all,
just three views onto registers that already exist. Anything that allocates a
colour must keep its three registers adjacent; `Emitter.ToColour` and
`Emitter.Combine` are the only places that do, and both allocate a block of three.

Narrowing a colour to a scalar is lossy and needs a rule. `ToScalar` uses
Rec. 709 luma weights rather than an average — the video-correct choice, and it
costs five ops.
