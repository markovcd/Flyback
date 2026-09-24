# ADR-0144: A pipe lands where the text says

**Status:** Accepted · 2026-09-24 · *user-directed* · amends
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

[0065](0065-a-text-language-that-parses-to-a-patch.md)'s pipe rule had three
clauses: an `in` takes the signal, else a leading `x` and `y` take a position,
else the first socket the call did not name takes it. The third decided where a
wire went from the arguments beside it. `hsv(saturation: 0.85, value: 1)` after
a pipe took it on `hue`. `hsv(hue: h, saturation: s, value: v)`, which is what an
unsteered agent wrote, took it nowhere. Naming one more knob moved the wire.

A pipeline could also sit inside an argument, `sine(freq: steps |> note())`,
and every agent that wrote the language bound its pipelines to names instead
([language-for-agents.md](../language-for-agents.md)). The nested form is what
`print` wrote, so an agent reading a printed patch read a dialect it did not
write.

## Decision

**`socket: _` says where a pipe lands.** A pipe lands on the socket an argument
gives as `_`, else on `in` or a module's only socket, else on the module's own
first two sockets when they are `x` and `y`, the call names neither, and what is
piped is a position, else on the module's one color socket when what is piped
is declared a color. Which sockets a bare pipe lands on is a fact about the
module and the source, never about the arguments beside it. Anything else is refused, and the
complaint names the socket to write: `'adsr(gate: _)'`. `_` goes in a named
argument, once. In a `def`'s call it stands for the parameter it is written in
place of.

This was proposed as a pipe socket declared on every module. `_` gives the same
guarantee with no field on `NodeDef` and no pass over the catalog: the landing is
on the line, so a reader, a model or a diff sees it without knowing the module.

**A pipeline is never an argument.** One inside an argument, bracketed or not,
is an error that says to bind it with `let`. Arithmetic in an argument stays.

**`print` writes the same dialect.** It writes `_` where the landing is not `in`,
an only socket, a pair or a color's one socket, and binds to a `let` anything that would otherwise be a pipeline
inside an argument.

## Consequences

- Twenty-four built-in modules have neither `in`, a single socket nor a
  leading pair — Note, ADSR, Chance, Duck, Gain, HSV, the two-input Maths — and
  a pipe into any of them writes `_`, except a color into Gain, Ink or
  Vignette. That is longer, and it is the point.
- Every `.fbks` in the docs, the site, the handbook and the tests was rewritten,
  and each compiles to the same program as before.
- `flyback-cli modules <module>` marks the sockets a bare pipe lands on, and for
  a module with none says to write `_`.
