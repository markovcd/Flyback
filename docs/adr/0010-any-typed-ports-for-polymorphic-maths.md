# ADR-0010: `Any`-typed ports make maths modules polymorphic

**Status:** Accepted · 2026-08-11

## Context

`Multiply` should work on two scalars, on a colour and a scalar, and on two
colours. So should `Add`, `Mix`, `Clamp`, `Maximum` and the other twenty-odd
maths modules. This is exactly the overloading a shading language gives you for
free.

With only `Scalar` and `Colour` port kinds, the compiler must coerce every input
to its declared kind. A `Multiply` with `Scalar` ports would silently narrow an
incoming colour to luma — quietly destroying the image. A `Multiply` with
`Colour` ports would widen every scalar to three components, tripling the op
count for the scalar maths that makes up most of a patch.

The alternative is two modules per operation — `Multiply` and `Multiply (colour)`
— which doubles the catalogue and makes the user think about types.

## Decision

A third port kind that opts out of coercion:

```csharp
public enum PortKind { Scalar, Colour, Any }
```

The compiler coerces typed ports and passes `Any` through untouched:

```csharp
inputs[port] = spec.Kind == PortKind.Any ? value : emitter.Coerce(value, spec.Width);
```

Width resolution then falls to `Emitter.Binary`/`Ternary`, which take the wider
operand and broadcast the narrower
([0007](0007-register-slots-with-scalar-broadcast.md)).

## Consequences

One `Multiply` handles every combination, and the output width follows the
inputs automatically. The maths category is 26 modules instead of 52.

Type resolution happens at compile time per patch, so there is no runtime cost —
a scalar multiply emits one op, a colour multiply emits three, and the
interpreter cannot tell the difference.

Ports of the three kinds are drawn in different colours, so an `Any` socket is
visually distinct from a committed one. Wires take the colour of their source
port, which means a wire carrying a colour looks different from one carrying a
scalar.

The looseness is real: nothing stops a colour being wired into `Rotate`'s
`angle`. It is a `Scalar` port, so it narrows to luma and produces something
arbitrary but harmless. This is the same permissiveness a modular rig has, where
any signal fits any jack, and it is deliberate — but it does mean the editor
cannot reject a nonsensical patch, only render it.

`Any` is not inferred backwards. An `Any` output's width is whatever its inputs
produced; there is no unification pass, so a module cannot say "my output is a
colour only if input B is". Nothing in the catalogue needs that.
