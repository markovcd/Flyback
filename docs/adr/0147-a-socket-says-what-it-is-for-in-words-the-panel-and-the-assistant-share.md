# ADR-0147: A socket says what it is for, in words the panel and the assistant share

**Status:** Accepted · 2026-09-24 · *user-directed* · builds on
[0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md) and
[0122](0122-the-panel-wears-the-block-it-is-about.md)

## Context

A module's description was one paragraph that did two jobs. It said what the
module is for, and then went through its sockets one by one: "'cutoff' is in
hertz and is meant to be swept… 'resonance' peaks the corner…". The inspector
showed the whole paragraph above the knobs, so what one knob does was read far
from the knob, and the assistant got the same paragraph in its briefing with
the socket list printed separately above it. The outputs had no place on the
panel at all, so what a module puts out was only in the prose.

## Decision

**What one socket is for lives on the socket.** `PortSpec.Help` is a sentence or
two about that socket alone, written to stand on its own. The description keeps
what the module is, how its sockets work together, which sinks it reaches and
what is set on the node.

**One text, read in two places.** The inspector shows the description at the
top as before, each input's help as the tip on its row, and lists the outputs
after everything that can be set, each with its help as its tip.
`describe_module` and `flyback-cli modules <module>` print the help beside the
socket. So help does not open with the socket's name, which the reader sees next
to it in both places.

**The briefing says what modules are for, not sockets.** The assistant's
briefing carries each module's description and its socket list, and no socket's
help; `describe_module` is always offered, and is where the assistant reads what
a socket is for. Help on every socket is more than the briefing's budget
should carry up front for words the assistant needs only on the module it is
about to patch.

**Every socket says what it is for.** No socket ships without help, outputs
included; a shipped module with a silent socket fails its scenario.

**A socket that means the same everywhere is described once, and opts in.**
`SocketHelp` holds the standard texts as constants — a position's `x` and `y`,
an oscillator's `freq`, `phase`, `amp` and `bias`, an effect's `mix`, a domain
input whatever it is called — and a socket takes one by setting its help to it,
usually in the helper that builds the socket. Nothing is filled in by name, so a
socket whose name means something else on its module never shows words that are
wrong for it.

**Help is an init property**, the way `Knee` and `Lenient` are, so a plugin built
against the earlier contract loads unchanged, with no help on its sockets.

## Consequences

- The briefing's cost for a module is its description, and the budget of
  [0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md) leaves it
  out or keeps it. Socket help costs the briefing nothing, and a module the
  assistant patches costs one lookup.
- A standard text is shared by the sockets that name it and by nothing else, so
  adding one changes no socket until a socket takes it.
- `find_modules` searches help as well as descriptions.
- Every socket has a tip, even where the name nearly says it.
