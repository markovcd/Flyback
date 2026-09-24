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
after everything that can be set, each with its help as its tip. The assistant
reads the description followed by `name: help` for every socket with help of
its own, and `describe_module` and `flyback-cli modules <module>` print the help beside
the socket. So help does not open with the socket's name, which the reader sees
next to it in both places.

**A socket that means the same everywhere is described once.** `SocketHelp`
lists the standard sockets by name, inputs and outputs apart — a position's `x`
and `y`, an oscillator's `freq`, `phase`, `amp` and `bias`, an effect's `mix` —
and every domain input, whatever it is called. A socket with no help of its own
takes the standard for its name, so the inspector shows it as it shows any other
tip, and a plugin's socket gets it without saying anything. A socket whose name
means something else on its module writes its own help, which wins. The
assistant is told the standard list once, ahead of the modules, and a module's
socket line only where its help is its own; `describe_module` and the command
line say the whole of it, since they answer about one module.

**Help is an init property**, the way `Knee` and `Lenient` are, so a plugin built
against the earlier contract loads unchanged and has the standard help only.

## Consequences

- The briefing's cost for a module is its description plus its own socket
  lines, and the budget of [0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md)
  leaves out both or neither. The standard list is a fixed cost, paid once.
- A name added to the standard list gives its text to every socket of that name
  with none of its own, so a name goes on the list only where it means the same
  on every module that has it, or where the ones that differ say so.
- `find_modules` searches help as well as descriptions.
- A socket whose name says everything gets no help; the tip is only there when
  there is something to say.
