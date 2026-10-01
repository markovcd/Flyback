# ADR-0161: A turn is the host's, and a provider writes only its format

**Status:** Accepted · 2026-09-29 · *user-directed* · amends
[0066](0066-a-second-wire-format-so-one-model-can-hear.md)

## Context

The Gemini and chat-completions adapters each held a copy of the turn: reopen the
workbench, drop earlier pictures, ask up to forty times, answer every call, stop
dispatching after a proposal, say when a turn changed the patch and offered
nothing, map each tool outcome to the event the panel paints. They held the same
listening brief word for word, and the same retry loop, whose list of retryable
statuses had already drifted apart.

None of that is either format's. It is what a turn promises the panel, and two
bugs in it (a stop leaving calls unanswered, the "not offered" note on turns that
changed nothing) were each fixed twice, identically. A third adapter would have
had to find every one again, the first of them as a 400.

## Decision

**The host runs the turn.** `AssistantRun` runs `TurnLoop` over any session that
is an `IModelConversation`, and never calls that session's own `Ask`, so what a
turn promises holds whatever a provider does. `TurnLoop` yields events as they
happen, so each edit still reaches the window the moment it is made; that was the
reason each adapter wrote its loop out, and a shared iterator keeps it.

**A provider implements `IModelConversation`, which is its format and nothing
else:** add the person's message, send and keep what came back, add the answers
with their pictures and clips, drop earlier media, play a clip to an ear, and say
which model listens and whether its own model hears. It hands back the workbench
it was started with, so the loop needs nothing from the provider's settings.
`ToolCall`, `ModelReply` and `ToolAnswer` are the neutral shapes that cross it.

**It is an `IPatchSession`, so `Start` and `Resume` are unchanged.** It answers
`Ask` itself, with the same loop, for a caller that asks anyway; a provider writes
no `Ask` of its own.

**`AssistantPost` sends one request and waits out what should be waited out.**
Where a refusal says how long to wait, and how it words itself, stay each format's
to read.

> **Amended.** *(2026-10-01.)* A wait is told while it is waited. A
> tokens-per-minute limit can hold a request for a minute, and a transcript
> silent for that long looks hung. `AssistantPost` tells each wait to the turn
> through an internal hook that flows with the request, so no provider passes
> anything on and the contract is unchanged, and `TurnLoop` yields it as a
> `Did` line ("waiting 31s for the rate limit") while the request still waits.
> A wait of under a second is not told, and a turn that only waited before it
> failed is not counted.

> **Amended.** *(2026-10-01.)* A turn with no ear says so when asked to hear.
> Told in the briefing that it cannot hear, a model asked to "listen and bring
> it to -16 LUFS" still proposed a patch "to reach -16 LUFS" it had never
> measured. Where the workbench offers no `listen` and the message asks to
> listen, hear, or for a loudness, the turn opens by telling the person, and
> the message reaches the model with a note saying the same. The words are
> matched, not understood, so a request that names none of them gets the
> briefing alone.

**The listening brief is the host's** (`TurnLoop.Ear`): what to ask about a synth's
clip is knowledge of Flyback, not of a provider.

**A provider whose conversation is some other shape implements `IPatchSession`
alone** and writes its own `Ask`, as before. That is the one way left to run a
turn outside the host.

## Consequences

**The two adapters share code now.** ADR-0066's "shares no code" still holds for
the wire formats, which line up no better than they did; what is shared is the
turn around them.

**The plugin contract grows.** The new types are public in `Flyback.Plugins`,
which is a minor step of the contract version (ADR-0102).
