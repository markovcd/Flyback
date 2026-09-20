# ADR-0104: A formula is one block, and exactly the modules it names

**Status:** Accepted · 2026-09-19 · *user-directed* · follows
[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md),
[0055](0055-a-plugins-extra-declares-its-editor.md) and
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md) and
[0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)
took the patterns the presets repeat and made each a module. They were right to
count before building, and the count has a floor: a wrapper is only worth it for
something built three times over. Most arithmetic is built once.

Overworld shows how much that leaves. Its picture is a pixel grid, a bulging
screen, a sky banded by the time of day, bricks, blocks, coins and a running
figure, and nearly all of it is arithmetic written once: a column is
`floor(x * 45)`, a pixel's middle is `(column + 0.5) / 45`, the mortar is the
larger of two thresholds on two fractions. That is about three hundred Maths
modules, which is more than half the patch. A reader opening the Ground box finds
twelve of them for one idea, and a box shut to fit the canvas
([0092](0092-a-drawing-too-wide-for-the-canvas-shuts-a-box.md)) hides them
without making them any easier to read.

[0004](0004-visual-patch-editor-as-the-authoring-model.md) chose a node editor
over live-coded expressions as the way a patch is made, and
[0065](0065-a-text-language-that-parses-to-a-patch.md) later added a text
language that parses to that patch. Neither says what a module inside the node
editor may hold.

## Decision

**An Expression is one module with four sockets and a formula.** The sockets are
`a`, `b`, `c` and `d`, untyped as every Maths socket is
([0010](0010-any-typed-ports-for-polymorphic-maths.md)), so a formula works on a
color a channel at a time. The formula is text the module carries: `+ - * / %`,
a minus in front, brackets, numbers, `pi` and `tau`, and a call for every Maths
module that lowers to ops alone, named by its type id without `math.` — `sin`,
`floor`, `smoothstep`, `remap` and so on, with Mixer and Desk left out because
they are rows of channels. An argument left off is that module's knob at rest,
so `clamp(a)` is a Clamp between minus one and one.

**It is the modules it names, and nothing else.** Every operator and every call
is lowered by that module's own emit function, handed what the formula wires into
it. There is no evaluator and no opcode, so 0095's condition holds by
construction rather than by care: a formula is the patch that wires its modules
by hand, op for op. Two things make that true of numbers too. Two numbers either
side of `+ - * /` are folded into one on floats, the way a knob holds one, so
`1 / 45` is the number a C# preset's `1f / 45f` put on a knob. And a minus in
front of a number is a negative number, not a Negate. A part written twice is
lowered once, as one module wired to two places would be.

**The formula is a field, so nothing new has to know about it.** It is declared
as a new field shape, `ExtraField.Text`, on the route
[0055](0055-a-plugins-extra-declares-its-editor.md) laid for a plugin's settings.
The panel draws it as a line to type into, kept on Enter or when the focus leaves
and put back by Escape. The text language writes it as a named string, and the
assistant sets it with `set_extra`. It is the first shape added since the
contract was written down, so the plugin contract moves to 1.1.0
([0102](0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)).

**Its own reader, in Core.** The text language's parser is in the Engine and a
module is lowered by Core, so the formula has a small recursive-descent reader
of its own ([0019](0019-no-third-party-dependencies-in-the-engine.md)). It knows
the arithmetic half of the language and nothing else: no names but the sockets,
no `let`, no pipe.

**A formula that does not read is a complaint and a nought.** The complaint says
what stopped it and at which character, and is an error. The module outputs
nought until the formula reads, which is the bargain a missing file has with a
Sample.

**The formula is written in the body, and the header is a name.** The body draws
it beside the socket letters, wrapped over as many lines as there are, cut with an
ellipsis and tipped in full when it is longer than that. The header is the module's
name, as on every other module: an Expression is called Expression until somebody
renames it, and `Title` is the name it was given or its definition's. Nothing
measures a formula to decide what a module is called, so the panel's title, a
compiler's complaint and a shut box's socket say what they would for a Sine. One
layout, whatever a formula's length and whether the module has a name.

## Consequences

**Overworld is written in formulas.** 510 modules become 306, and both of its
programs are the same size they were. Eight stills rendered across the track are
the same bytes before and after, and so is the whole track rendered to a WAV.
What is left as Maths modules stands alone, one module where a formula would only
rename it.

**A number in a formula is not a knob.** It cannot be turned, followed by a
panel knob ([0086](0086-panel-knobs-are-read-as-live-values.md)) or ridden from
a controller. A value somebody should play belongs on a socket, where it is one.
This is the price of the block being one block, and the description says so.

**A canvas of Expressions is a column of identical headers.** What tells them
apart is the formula under it, which is the line worth reading anyway. A patch
where that is not enough gets names, which is what names are for. A shut box's
output socket says `Expression.out`, and an input takes the name of what feeds it.

**The primitives stay the specification.** Nothing is removed, and a formula
means what the modules it names would have done. The tests that say so build the
long form beside a formula and compare the two programs op for op, and then the
pictures they draw with equality.

**The formula is not the text language.** It reads like the language's
arithmetic and is not a subset of it: `x` and `t` mean nothing inside a formula,
because a formula reads its sockets and the patch wires the signals in. Folding
the two readers into one would put the Engine's parser in Core for the sake of
four sockets.
