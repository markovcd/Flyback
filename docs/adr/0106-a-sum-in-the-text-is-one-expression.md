# ADR-0106: A sum in the text is one Expression

**Status:** Accepted · 2026-09-19 · *user-directed* · follows
[0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md)

## Context

[0065](0065-a-text-language-that-parses-to-a-patch.md) gave the text language
infix arithmetic as sugar for five Maths modules. `+ - * / %` were Add,
Subtract, Multiply, Divide and Modulo, and a minus in front was Negate. That
kept the language a thin spelling of the catalogue. It also meant a line like
`(fract(t * 60) * 2 - 1) * aspect` came out as a column of four Maths modules
and a Fraction, which is the shape
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) made the
Expression to replace on the canvas.

An Expression's formula is the same arithmetic, over sockets instead of names.
A sum written in the text already is one.

## Decision

**A sum is one Expression.** The binder reads a whole tree of infix arithmetic
before placing anything. The numbers in it are written into the formula. The
signals in it become the sockets, each once however often it is read, lettered
in the order the formula first reads them. A call inside the sum, such as
`fract(...)`, is still the module it names and is read as a signal. Two numbers
together are still folded into one before any of this, so `1 / 12` is a number
and not a formula.

**More than four signals is more than one Expression.** A tree reading more
signals than an Expression has sockets hands its busier side to an Expression
of its own, and reads that as one signal, until it fits.

**Brackets are written wherever the tree needs them.** The formula groups as the
text did, including on the right of an operator as strong as itself. Floats do
not reassociate, so `a + (b + c)` stays bracketed.

**An Expression asks for each socket as its formula reaches it.** A module is
otherwise handed all its inputs before it is entered, so a formula's operands
were all lowered first and then its ops. The modules it names lowered each
operand where it stood. The values were the same, but the programs were not the
same op for op. `NodeDef.AsksForItsInputs`, internal to the engine, lets a
module lower each input when it asks for it, under the walk's ordinary cache,
so a signal read here and elsewhere is still one register. Unlike a swept input
([0040](0040-a-probe-is-a-second-compile-root.md)), it does not scope the cache.
It only changes when the input is lowered. A socket the formula never reads is
never lowered at all.

## Consequences

**The reference texts build the programs they did.** The text forms of the
engine's presets are compared with the presets as the C# builds them, op for op
and register for register, and they still match without a change to either. A
text patch has fewer modules and the same program.

**The printer still writes Expressions as calls.** A patch printed from the
canvas writes an Expression as `expression(…, formula: "…")`, which reads back
to the same patch. Writing it back as infix would read better. It is not done
here because the printer places modules by lining up the calls it wrote with the
calls it reads back, and an operator is not a call.

**A number in a sum is not a knob.** Before, `t * 0.2` put 0.2 on a Multiply's
knob, and turning that knob in the panel wrote the new number back into the
text. Now 0.2 is in the formula, as
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) has it,
and a number that should be turned belongs in a `value(...)` or a knob of the
module it feeds.

**Maths modules are still written by name.** `mul(a: x, b: y)` is still a
Multiply, and every patch that says so builds what it did.
