# ADR-0112: A formula is read in the body, and the header is a name

**Status:** Accepted · 2026-09-20 · *user-directed* · supersedes the naming
decision in [0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md)

## Context

[0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) put the
formula in the header, because an Expression called Expression says nothing. The
header is one line of 12.5pt across 196 pixels, so a formula longer than that
fell back to the body under the word Expression, and a formula on a module
somebody had named went to the body too. Three layouts, decided per module by a
width measurement: where the formula is depended on how long it is.

[0108](0108-a-preset-arrives-with-its-arithmetic-folded.md) and
[0109](0109-the-expression-stands-for-the-small-maths-modules.md) then made the
Expression the ordinary way to write arithmetic — 662 of them across the shipped
presets — so which layout a module got was the common case rather than the
corner one, and a canvas read as a mix of the three.

## Decision

**The formula is always written in the body.** Beside the socket letters, wrapped
over as many lines as the body has, cut with an ellipsis and tipped in full where
it is too long for them. One layout, whatever the formula's length and whether
the module has a name.

**The header is the module's name, as on every other module.** An Expression is
called Expression until somebody renames it, and `Title` is the name it was given
or its definition's — nothing measures a formula to decide what a module is
called. The panel's title, a compiler's complaint and a shut box's socket all say
the same thing they say for a Sine.

## Consequences

**Nothing else asks where the formula is drawn.** The header, the panel title and
the tooltip each had to agree with a width measurement so the formula was said
once; now the header draws a title and the body draws a formula, and neither
consults the other.

**A canvas of Expressions is a column of identical headers.** What tells them
apart is the formula under it, which is the line that was worth reading anyway. A
patch where that is not enough gets names, which is what names are for.

**A shut box's output socket says `Expression.out`.** It said the formula before.
An input still takes the name of what feeds it, since the socket letter is no
more use than the module name is.
