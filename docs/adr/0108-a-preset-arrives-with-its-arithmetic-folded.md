# ADR-0108: A preset arrives with its arithmetic folded into Expressions

**Status:** Accepted · 2026-09-19 · *user-directed* · follows
[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) and
[0070](0070-a-preset-declares-no-coordinates.md)

## Context

[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) moved
Overworld onto Expressions by hand, one formula at a time, with each constant
spelled the way C# had folded it. The other presets still built their arithmetic
as Maths modules, 2,702 modules across 47 presets. Porting each by hand would have
meant hundreds of formulas written and checked one at a time.

## Decision

**Every preset is folded as it is built.** `ExpressionFusion.Fuse` finds each
chain of Maths modules in which every module feeds only the next, and replaces it
with one Expression, under the id of the module the chain ended in. The formula
is written from the modules themselves: an operator for Add, Subtract, Multiply,
Divide and Modulo, a call for the rest, and each knob as the number it held.
`Presets.Fused` wraps a preset so that it is folded and then laid out again
([0070](0070-a-preset-declares-no-coordinates.md)). The engine's presets are
wrapped where they are listed, and the plugin host wraps every preset a plugin
offers.

**It is exact, and it is checked.** An Expression lowers through the modules it
names, in the order their walk would have
([0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md),
[0106](0106-a-sum-in-the-text-is-one-expression.md)). A literal is written as the
float the knob held, and a module fed no wire at all is never folded, so no two
numbers meet for the formula's reader to fold. Every preset was dumped before the
change and compared after it. All 47 compile to the same programs, op for op and
register for register, for the picture and for the sound.

**Some modules are left as they are:**

- a module of more than two inputs, since a Clamp, a Mix, a Smoothstep or a
  Remap names what each number on it is;
- a module a panel knob follows, since turning it is what the knob is for;
- a module in a loop, since what it reads is the evaluation before;
- a module with a name, one fed no wire at all, and the Mixer and the Desk.

A chain stops where taking in the next module would read a fifth signal, or
would make the formula longer than sixty characters. That is about as much as a
line of text and a module's title can carry.

## Consequences

**The presets are smaller and read as their arithmetic.** They come to 2,251
modules in total, with 645 Expressions. The showcases shrink by a seventh to a
third: Whole band from 232 to 182, and Phase from 199 to 137. Overworld barely
moves, because it was written in formulas already. The C# that builds
them is unchanged and still says `Times` and `Plus`: the source describes the
arithmetic, and folding is how it arrives.

**A number that was on a knob is now in a formula.** It is edited by editing
the formula rather than by turning it. What a player should turn is on a panel
knob, and those modules are left alone.

**The printer writes a sum whose operand is a pipeline as a call.** A pipeline
in brackets inside a sum is the one line the text layout cannot fold, and Whole
band printed one wider than the page. Such an Expression is now written as its
call, with the pipeline piped into it. That narrows
[0107](0107-an-expression-is-printed-as-the-sum-it-is.md): `fract(t * 60)` in a
sum reads back as `t * 60 |> fract() |> expression(...)`.
