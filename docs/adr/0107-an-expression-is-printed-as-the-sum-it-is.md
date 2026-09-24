# ADR-0107: An Expression is printed as the sum it is

**Status:** Accepted · 2026-09-19 · *user-directed* · follows
[0106](0106-a-sum-in-the-text-is-one-expression.md) and
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

[0106](0106-a-sum-in-the-text-is-one-expression.md) made a sum in the text one
Expression, and left the printer writing every Expression as a call:
`t * 0.2` read in and printed back as `t |> expression(formula: "a * 0.2")`. The
reason given was the source map. The printer finds where each module stands by
lining up the calls it wrote with the calls it reads back, and an operator is
not a call.

## Decision

**An Expression is written as its arithmetic wherever that reads back as the
same module, and as its call everywhere else.** The formula is spelled in the
language's operators, with each socket replaced by what is wired into it. Five
things keep it a call, because each would read back as something else:

- a function, `pi` or `tau` in the formula, since a call in the language is a
  module of its own and the other two are words it does not have;
- a socket the formula reads that rests on its knob, which would come back as a
  number written into the formula;
- a wire into a socket the formula does not read, which the sum would drop;
- an Expression written out in full on one of its sockets, since the two sums
  would read back as one;
- a module written out in full on a socket the formula reads twice, which would
  read back as two modules.

A number is written the way the printer writes any number, so it reads back as
the same float. Brackets go where the formula's tree needs them, by the rule the
binder follows.

**A sum is where the module is.** The source map counts the outermost operator
of a sum that reads a signal as the place its Expression stands, in a printing
and in typed text alike. The modules on its left come before it and those on its
right after, which is the order the printer emits them in. The operator is also
something to click: the caret on it selects the Expression, and the caret on an
operand selects that operand.

**A formula edited in the panel prints the reading again.** A sum has no
`formula:` argument to write a new formula into. Over a printing, the app prints
the patch again instead of reporting a value it could not place. Over text
somebody wrote, it reports it, as it does for any value the text has no place
for.

## Consequences

**A printing reads as the sum.** Overworld prints with 44 of its Expressions
still as calls, all of them because their formulas call a function. Read back,
it builds the same 306 modules, and renders the same pictures and the same
sound. Printing the printing again gives the same text.

**The printer writes a module's input as a pipe, as before.** `fract(t * 60)`
read in comes back as `(t * 60 |> fract())`. That is how the printer writes
every module whose input is wired, and nothing here changes it.

## Amendment, 2026-09-24: functions and panel knobs are written in the sum

A call in the text to a Maths module of one or two sockets is fused back into
the Expression it sits in ([0109](0109-the-expression-stands-for-the-small-maths-modules.md)),
so a formula's function has a spelling after all: the call, with every argument
written, `pow(1 - abs(fract(a - b + 0.5) - 0.5) * 2, 6)` over what is wired in.
A socket that follows a panel knob is written as the knob, which the binder now
reads in a sum as a socket of its Expression.

The call stays where the function would read back as something else: a Maths
module of three sockets or more, which the fusing keeps as a module; one call
written twice, which the formula computes once and the text would place twice;
a formula longer than a fused one may run; and a module switched off, which
would pass the called module on instead. The source map counts each call in a
sum as the Expression's, with no brackets for a knob.

Across the shipped presets the calls left fall from 284 to 54.
