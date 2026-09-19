# ADR-0109: The Expression stands for the small Maths modules

**Status:** Accepted · 2026-09-19 · *user-directed* · follows
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) and
[0108](0108-a-preset-arrives-with-its-arithmetic-folded.md), and widens what
[0108](0108-a-preset-arrives-with-its-arithmetic-folded.md) leaves alone

## Context

[0108](0108-a-preset-arrives-with-its-arithmetic-folded.md) folded the presets'
chains of Maths modules into Expressions and left 356 Maths modules standing:
those with a panel knob, on a loop, with a name, fed no wire, and the ones a
chain could not reach. The palette still listed Add, Multiply, Floor and the rest
beside the Expression, a call like `mul(...)` or `fract(...)` in the text still
built the module, and the assistant was still told about every one of them. Two
ways to write the same arithmetic, and the one that makes the canvas longer was
the one on offer.

Nobody has written a plugin or kept a patch that depends on those modules, so
nothing has to go on opening as it did.

## Decision

**The Expression stands for every Maths module of one or two inputs that lowers
to ops alone:** Add, Subtract, Multiply, Divide, Modulo, Power, Minimum, Maximum,
Atan2, Length, Absolute, Negate, Sin, Cos, Tan, Square root, Floor, Fraction,
Sign, Exp, Log and Threshold. The modules stay in the catalogue, because an
Expression lowers through their emit functions and they are its functions. What
changes is what a person or the assistant is handed:

- **Folding reaches all of them.** A module with a name becomes an Expression
  with that name. One fed no wire becomes an Expression over its knobs. One on a
  loop becomes an Expression of its own, and nothing on a loop folds into
  anything else, because which wire of a loop carries the evaluation before is
  decided by the graph's shape, and a chain folded on one moves it. Expressions
  fold into one another the way the modules they stand for do.
- **Numbers are written into the formula, with two exceptions.** A number a panel
  knob follows stays on a socket, and the socket follows the knob. Two numbers
  either side of `+ - * /` stay on sockets too: the formula's reader adds them as
  floats, where the program adds them in its registers.
- **The text language folds as it reads.** A call like `mul(...)` or
  `fract(...)` builds the module, and the patch is folded before it is laid out,
  so the call joins the sum around it. What the text said about a module folded
  away now points at the Expression it went into.
- **The palette and the assistant hand out the Expression.** The palette lists
  none of the 22 until somebody types one's name, and then offers it as the
  Expression it is (`Multiply: a * b`). The canvas and the assistant's
  `add_module` turn a request for one into an Expression with the module's
  formula over sockets and its knobs as they rest. The assistant's briefing
  leaves them out, since their names are the Expression's functions.

A Clamp, a Mix, a Smoothstep, a Remap, a Mixer and a Desk are modules still.
Each has more than two inputs, and the names on its knobs say what each number is.

## Consequences

**No shipped preset has any of the 22,** and a test says so. All 47 presets
compile to the programs they compiled to before any folding, op for op and
register for register. They come to 2,253 modules, with 662 Expressions.

**A text read twice is not always printed the same way the first time.** A
printing drops the boxes, and Expressions either side of a box's edge that a
preset kept apart fold together when the text is read. From the first reading
on, the text is stable, and that is what the round-trip test holds it to.

**A knob on a call that was folded cannot be written back into the text.** Its
brackets take no formula, so turning the Expression's formula in the panel over
text somebody wrote is reported, as any value the text has no place for is.
Over a printing, the text is printed again.
