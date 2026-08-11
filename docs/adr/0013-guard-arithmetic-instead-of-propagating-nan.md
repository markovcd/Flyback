# ADR-0013: Guard arithmetic instead of propagating NaN

**Status:** Accepted · 2026-08-11

## Context

A patch is edited live, and intermediate states are frequently degenerate. A
`Divide` sits at zero until its divisor is wired up. A `Log` receives a signal
that swings negative. A `Power` gets a negative base and a fractional exponent.

IEEE 754 says these produce `NaN` or infinity, and both propagate: one `NaN`
anywhere upstream turns the entire image black and stays that way. The user sees
a dead screen with no indication of which of thirty modules caused it, and the
cause is often a knob they are mid-drag on.

The same values also poison the feedback buffer, so a transient `NaN` can persist
across frames after the offending edit is undone.

## Decision

Guard the ops that can produce non-finite values, at the point of evaluation:

```csharp
case OpCode.Div:  registers[op.Out] = Divide(registers[op.A], registers[op.B]); break;   // b == 0 -> 0
case OpCode.Sqrt: registers[op.Out] = registers[op.A] <= 0f ? 0f : MathF.Sqrt(...); break;
case OpCode.Log:  registers[op.Out] = registers[op.A] <= 0f ? 0f : MathF.Log(...); break;
case OpCode.Pow:  registers[op.Out] = Guard(MathF.Pow(...)); break;                      // non-finite -> 0
```

`Guard(v) => float.IsFinite(v) ? v : 0f` catches the general cases (`Pow`, `Exp`,
`Tan`, `Mod`). `Clamp` additionally tolerates an inverted range rather than
throwing. `Saturate` in the renderer is the final backstop: non-finite becomes 0
before anything reaches a pixel or the feedback buffer.

## Consequences

A half-built patch shows a plausible image instead of a black screen. Dividing
by zero yields zero, which is wrong mathematically but useful in practice — the
rest of the patch stays visible while the divisor is being wired up.

Feedback stays clean. A transient non-finite value cannot enter the history
buffer, so a bad edit does not leave a permanent stain that survives being undone.

This is a deliberate departure from shader semantics. A GPU backend
([0003](0003-cpu-rendering-with-a-gpu-path-left-open.md)) would not reproduce it
for free — these guards are branches, and matching them in GLSL means explicit
selects. A patch that relies on `1/0 == 0` would look different on the two
backends. That divergence is accepted; the alternative is a tool that goes black
while you use it.

Silence is the real cost. There is no indication that a guard fired, so a patch
can be quietly wrong — a `Log` clamped to zero over half the frame looks like a
flat region rather than an error. The status bar reports compile issues but not
runtime guards, and counting them per frame would be a reasonable addition.

`MathF.Sign` and integer overflow in the noise hash are not guarded, because
neither can produce a non-finite float. The hash relies on C#'s default
unchecked arithmetic.
