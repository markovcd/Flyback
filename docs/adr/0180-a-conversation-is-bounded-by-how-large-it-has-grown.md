# ADR-0180: A conversation is bounded by how large it has grown

**Status:** Accepted · 2026-10-06 · *user-directed* · replaces the turn limit that
[0113](0113-the-workbench-does-not-limit-how-large-a-patch-is.md) named as the
next ceiling, and builds on [0159](0159-a-turn-pays-only-for-what-is-new.md)

## Context

A conversation ended after a number of turns, 12 unless Settings → Assistant said
otherwise. Every request carries the whole conversation, so what a turn costs grows
with everything before it, and a turn may make up to 40 requests. Twelve turns of
talk about a small patch are cheap; two turns rebuilding Whole band are not. The
count stopped the first and let the second through, and could not see one turn
outgrowing the model's window.

## Decision

**The limit is tokens, counted from the newest request.** Each request's
`PatchEvent.Cost.Input` is every token it sent, cached ones included, and the last
one is how large the conversation has grown (`TokensSpent.Context`). A setting,
`ContextLimit`, 100k by default, 40k to 1M, bounds it; `flyback-cli ask --context`
overrides it for a run.

**It is checked before every request, not only between turns.** `TurnLoop` sends
nothing more once a request has reached the limit, after answering the calls that
request asked for, so a proposal in it still ends the turn. Between turns the run
refuses the next message, and the next one starts a new conversation. A saved
conversation keeps its context, so one carried on past the limit is not resumed.

**The default fits every shipped provider's window.** gpt-4o's is 128k; the
briefing alone is 22k to 34k.

## Consequences

A long conversation about a small patch carries on; a large rebuild stops sooner.

A provider that reports no tokens is not bounded by this, only by
`TurnLoop.MaxExchanges` within a turn. Every shipped one reports them.

A conversation that reaches the limit ends rather than shrinking. Cutting old tool
answers to a line, as pictures already are after their turn, would let it carry
on; that is a decision of its own.
