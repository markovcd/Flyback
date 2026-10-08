# The assistant spends requests and tokens it does not need

Found on 2026-10-01, on `main` at `890d0505`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Severity:** Medium
- **Status:** Open. The opening message now carries the patch, rendered frames say they are Flyback's, warnings name their module and an account out of credit fails at once; the four below are what is left.

## Evidence

Confirmed by running `flyback-cli ask --json` against the real endpoints, with one request, "a slow low drone with a circle that pulses in time with it", on a new patch:

| Model | Requests | Input tokens | Wall time | Proposed |
|---|---|---|---|---|
| gpt-4o (OpenAI's default) | 13 | 295k | 8m 01s, 7m of it rate-limit waits | yes |
| gpt-4.1 | 3 | 68k | 1m 08s | no |
| gemini-3.6-flash | 4 | 105k | 24s | yes |

Gemini on a follow-up, "make the circle blue": 3 requests, 87k in. Gemini on the Echo chamber preset, "repeats longer, room darker": 5 requests, 132k in.

`ask` now ends each turn with a `turn` line, so rerunning these is one command each.

## What is left

1. **The briefing is 22–24k tokens on every request.** The module list is 40k of its 62k characters and the preset list 7.7k. Cached input is cheaper, but OpenAI counts it against the tokens-a-minute limit, which on a low tier admits one request a minute: that is where gpt-4o's seven minutes went. The plan is below, under [A shorter briefing](#a-shorter-briefing).
2. **gpt-4o builds a module and a wire at a time.** It never called `write_patch`, though the handbook and both tool descriptions say to: 13 requests where gpt-4.1 took 3. Make gpt-4.1 OpenAI's default model (`OpenAiAssistant.cs`), and have `add_module` refuse on a bench holding only the Output, pointing at `write_patch`: one wasted request in place of eight.
3. **Small edits are rewritten whole.** Gemini answered "make it blue" and "two knobs and a filter" with `write_patch` (about 2.5k output tokens, every module renamed), then called `describe_patch` to read back what it had just written. `write_patch`'s answer carrying the patch as it now stands would save the reread.
4. **Smaller ones.** Gemini called `render` on a sound-only patch, a request to learn there was nothing to see; `render`'s description could say it draws `color` only. On OpenAI the request after a picture came back with 0 cached tokens both times it happened, 23k billed at full price; why is not known, and finding out means reading the request bodies.

## A shorter briefing

Measured on 2026-10-08 on the Acid preset: the briefing is 67k characters, of which the
modules are 44k (65%) and the presets 9k. Each module costs a line of sockets with defaults and
ranges and a line of description; `describe_module` already answers both and more, and
`find_modules` matches a query against names, categories, the search words, descriptions and
socket help, with no decision model in it. So the briefing could list modules as one line each,
`type id | name | category | words`, about 8k characters, and leave the rest to the two tools.

The trade is requests for tokens: a model that does not know a module has to look it up before
it uses one, and each lookup is a request that resends the context, cached. A model that
guesses instead is refused by the socket's name, and the refusal lists the sockets, which is a
lookup it did not ask for. Whether the whole costs less than 11k tokens a request is the
question, and it is measured, not argued:

1. Add a `Briefing` setting beside `ProseBudget` in Settings → Assistant, `full` or `short`,
   defaulting to `full`; `short` writes the one-line list and says, where the full list said how
   to read sockets, that `describe_module` gives a module's sockets and `find_modules` finds one
   by a word. `flyback-cli ask --set briefing=short` tries it for a run.
2. Run the three prompts in [Evidence](#evidence) each way on Claude Code and on Gemini, with
   `flyback-cli ask --json`, and count requests, input tokens, wall time, and whether the turn
   proposed something that compiles and does what was asked. One run each is not enough for a
   model that varies; three.
3. Keep `short` as the default only where it proposes as often and costs less across both; keep
   it as a setting either way, since a model on a tokens-a-minute limit has a reason the others
   do not.

The decision model has no part in this. Picking the modules a message likely needs and briefing
only those would read the message before each turn, but the briefing is sent once and cached,
and the detail a turn needs is better fetched by the tool than guessed by the host.
