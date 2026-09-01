# ADR-0066: A second wire format, so one model can hear what it built

**Status:** Accepted · 2026-09-02 · *user-directed* · closes the compromise
[0047](0047-the-agent-may-listen-where-the-model-can.md) recorded as forced;
implemented in `Flyback.Plugins.Gemini`; keeps the boundary
[0025](0025-platform-io-behind-loadable-plugins.md) drew and
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) kept

## Context

The chat-completions adapter is deliberately not "an OpenAI plugin". The
endpoint is a field, so one adapter reaches Groq, Together, Fireworks, DeepSeek,
xAI, OpenRouter, Ollama and LM Studio without knowing any of their names. That
is most of the market, and it is the reason nothing else was needed for a year.

What it does not reach is a *format*. Two exist that are not this one, and the
question of which to write second is not really a question about model quality —
it is a question about which one changes what this program can do.

[0047](0047-the-agent-may-listen-where-the-model-can.md) is the answer, and it
says so in its own words. It gave the agent an ear and recorded, at length, that
the arrangement was forced rather than chosen:

> The models that take a sound require *every* request to carry one. A
> conversation driven by one is refused on its first turn — before a patch
> exists, let alone a sound to listen to — and would stay refused on every turn
> until the first `listen`.

So the sound went to a second model, on its own, and what came back to the model
doing the building was words. Everything downstream of that is a cost:

- **Two requests and two models per `listen`**, the second at its own rates.
- **A second-hand account, which will agree with you if you let it.** The first
  version forwarded the `note` and was told, over three steady tones, that it
  was hearing a kickdrum, a hihat and a melody. The fix was to tell the ear
  nothing and to send the measurements — peak, rms, **crest**, sixteen slices —
  so that something in the reply could contradict it.
- **Two handbooks**, one per configuration, so turning hearing on re-pays the
  cached prefix.
- **The `listen` the patch actually wanted was never tried.** The first attempt
  put the clip into the conversation as a user turn after the tool result,
  exactly as a rendered frame goes. Nothing about that shape was wrong. It was
  refused because of what the endpoint would accept.

Google's `generateContent` accepts it. A sound is an ordinary part of a turn
there — the same slot a picture goes in, in the same request, to the same model,
alongside function calling. The shape 0047 wrote and threw away is the shape
this format takes.

## Decision

**A second adapter, not a second base url.** `Flyback.Plugins.Gemini` speaks
`generateContent` and shares no code with the chat-completions one. Almost
nothing lines up: turns are `contents` of `parts`, the roles are `user` and
`model`, the briefing is a `systemInstruction` beside the conversation rather
than its first turn, the model is named in the path rather than the body, a tool
call is a part rather than a field on the message, and a tool result is a part
of a user turn rather than a message of its own. About the only thing the two
formats agree on is that an error carries `error.message`. Hand-written JSON
over one POST, for the reason the first adapter is: an SDK arrives with a
dependency graph, and an assembly loaded off disk at run time has to keep its
dependencies to itself ([0019](0019-no-third-party-dependencies-in-the-engine.md),
[0025](0025-platform-io-behind-loadable-plugins.md)).

**The clip goes into the conversation.** `listen` renders and measures exactly
as before — the tool is the workbench's and is provider-neutral
([0018](0018-never-render-frames-on-the-ui-thread.md) still owns where it runs)
— and then the WAV rides back in the same user turn as the `functionResponse`
that answered the call, after it, beside any rendered frames from the same turn.
One turn rather than several, because a turn that rendered and listened produced
one set of observations about one patch.

**Whose ear it is becomes part of the contract, because the briefing has to say
so.** `Listener` replaces the workbench's `bool hearing` with three states:
`None`, `Another`, `Itself`. It changes the handbook's one configurable
paragraph, the `listen` tool's description, and what the tool says becomes of
its `note`. This is not tidiness. The second paragraph of each version is the
same warning aimed at opposite failures — a borrowed ear agrees with whoever
asked it, and one's own ear agrees with whoever built the patch — and a model
told the wrong one credits its own impression to a listener that was never
there.

**The shell works out which without learning a model name.** `AssistantRun`
reads `AssistantSchema.Known(config.Model)?.Hearing`, the same bool the settings
form already reads to decide which switches to offer. A provider knows what its
own models take; nothing above the plugin boundary knows one name from another,
which is what [0025](0025-platform-io-behind-loadable-plugins.md) drew and
[0047](0047-the-agent-may-listen-where-the-model-can.md) kept when it put the
capabilities in the schema.

**A model nobody wrote down borrows an ear.** `Known` returning null means
`Another`, and that is the safe direction rather than a guess: being wrong that
way costs a description, and being wrong the other way sends a sound to a model
that refuses it and loses every turn from the first `listen` onwards. The
second-model path is therefore kept in this adapter too, and reaches exactly the
case of a name typed ahead of the table.

**The ear disappears from the form when there is nothing to choose.** A model
that hears for itself has no second model, so `AssistantConfig.EarModel` is null
and the dropdown goes rather than greying out — a disabled control asks somebody
to work out why it is there, and this one has stopped meaning anything. What it
held stays in the settings and comes back at the next model that cannot hear.

**Effort is sent here, where it is not sent there.** The chat-completions
adapter says plainly why it stays silent: the parameter is model-specific and it
cannot know what endpoint it is pointed at, so a guess is a 400. This adapter is
pointed at one place, so the plugin holds a table of what each model's
`thinkingBudget` may be — Pro cannot be told to stop thinking and floors at 128,
the Flashes accept zero — and maps Low and High to the ends of it. Medium sends
`-1`, which is the model deciding for itself. A model not in the table gets no
`thinkingConfig` at all.

**The endpoint is not editable.** `BaseUrlEditable` is false, which is the
opposite call from the other adapter and follows from the same reasoning: there,
pointing somewhere else *is* the point; here, this format is spoken in one
place, and the fixed endpoint is what makes the capability and budget tables
worth trusting.

**Gemini sits below OpenAI on priority.** Priority decides what a fresh install
starts on, and starting on the provider whose key nobody has is worse than
starting on the one that was already there.

## Consequences

**A `listen` is one request, and the model hears the patch it built.** The
second model, the second bill and the round trip in the middle of a tool call
all go. What replaces them is the clip sitting in the history and being resent
each turn — at roughly 32 tokens a second, a four-second clip is about 130
tokens a turn, which is cheaper than the request it replaces was.

**The check the second-hand ear provided is gone, and only the numbers are
left.** 0047's real finding was not that a borrowed listener is unreliable — it
is that a description which *could not have come back wrong* is not evidence.
An ear told nothing could contradict the model. Its own ears cannot: they belong
to the thing that built the patch and knows what it was hoping for. So the
measurements stop being a lie detector aimed at somebody else and become the
only account in the loop that answers to nobody. The first-hand handbook says
this outright — *where the two disagree, the measurements win* — and it is more
load-bearing here than it was there, not less.

**Three handbooks now, where there were two.** All three are deterministic, so
the prefix cache still holds within a run
([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)), and
the cost is unchanged: a person who changes how they listen has changed the
cached prefix and pays to write it once more.

**The tool schemas met a stricter reader.** They are written once, in the
workbench, in ordinary JSON Schema, and what this endpoint takes is a subset of
OpenAPI. Two differences bite. An object with no properties is refused, so a
tool that takes no arguments — `describe_patch`, `reset` — must declare no
parameters at all. And every property must have a type, which `set_extra`'s
`value` deliberately does not, because it takes a number, a boolean or a
choice's id depending on the field it is aimed at; it becomes an `anyOf` over
the three. Both are translated in the adapter rather than in the workbench,
because the workbench does not know which endpoint is reading it. Both fail as a
400 naming a field, from an endpoint that cannot say which of twenty tools it
was reading, which is why the declaration of every shipped tool is a test.

**Thinking is counted and not shown.** Reasoning comes back as text parts
flagged as thought and is skipped: it is not the answer, and a transcript meant
to hold what was decided should not fill with the working out. Its tokens are
added to the output count anyway, because they are billed and `PatchEvent.Cost`
is the only place anybody would see them.

**A model list is a thing that goes stale**, exactly as 0047 said. Three models
are written down and a fourth released next month is a stranger here. The
failure stays in the safe direction — a stranger is offered everything and
refused nothing — with the one exception above: a stranger gets no thinking
budget, because there is no telling what range it would accept.

**What this does not do:** nothing is asked for in sound, so the reply is still
a function call rather than speech; `listen` still renders the sound alone, so a
patch whose picture and sound are one thing is examined through two tools; and
this format takes video, which would let the agent see movement rather than
three stills, which nothing here does yet.
