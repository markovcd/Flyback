# ADR-0227: Arrangements chain by handing on their parts

**Status:** Accepted · 2026-11-06 · *user-directed* · amends
[0153](0153-an-arrangement-is-a-grid-of-parts-on-the-node.md)'s "section one's before is the last section"

## Context

An Arrangement holds at most 32 sections, a budget on op count (ADR-0038). A
piece longer than that had no way through but longer sections: the assistant,
writing a song over the MCP server, made its sections two bars long to fit. The
user asked for Arrangements that chain, the next picking up where the last ends.

The engine is stateless (ADR-0031), so one Arrangement cannot tell another it has
finished; whatever the second knows has to arrive on a wire. And a wire is one
value, while a chain has to carry every part.

## Decision

**An Arrangement hands on what comes into its parts until it starts.**

- **`next`**, a new last output, is `in` with the Arrangement's length taken off:
  nought where its last section ends. Wired into a second Arrangement's `in`, it
  starts that one there, at the second's own `rate`.
- **`part 1` to `part 8`**, new last inputs with no knob, are each part's level
  before it starts. Wired from the Arrangement before, the second one's parts are
  the whole piece, as a Desk's `bus` carries the Desk before it.
- **Before it starts**, while `in` is below nought, each part is its input, and
  `progress` and `section` are nought. A part it does not have holds at nought
  once it starts.
- **Section one comes from the input the first time through**, so the change into
  the second Arrangement fades or glides from the first's last level. After that
  it comes from the last section, as it did.
- **The last of a chain loops on its own**, as one Arrangement does. Looping the
  whole chain would need the first to know the last's length, which only a wire
  backwards could tell it.

A compile-time join, where wiring `next` made the second read the first's grid,
needed one wire rather than nine but would make a wire mean something other than
its value. Raising the cap was not what was asked, and pays the op budget in one
module.

## Consequences

A chained piece is three wires for three parts plus `next`, written once in the
text. Every Arrangement costs one op more for each part that changes and one for
each that holds or is missing, and a fresh one fades in from its inputs, nought
unwired, over `fade` at the very start rather than from its last section.
