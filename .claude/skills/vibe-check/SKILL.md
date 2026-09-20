---
name: vibe-check
description: Check a project against what a model reaches for when it is not allowed to look things up. Use when asked what is missing, what to build next, what should exist but does not, to find a roadmap, or to audit a project's vocabulary for holes — the names an unchecked agent invents are the holes. The same instrument settles a design fork: give unsteered agents one task and read which shape they converge on, for weighing options or being stuck between approaches. Not a review of code that exists — for how a diff or a design feels, use `/ick` or the taste-check agent instead.
---

# Vibe Check

A model writing about your project from instinct names the things it expects to
find. Where a name misses your actual vocabulary, you have found a hole that
everything else in its training fills and you do not.

Not to be confused with `/ick` and the `taste-check` agent, which take the same
English phrase. Those read work that exists and say how it feels. This one reads
what an agent reaches for and says what is not there.

The mistake is the measurement. You are not reviewing the agent's work — you are
reading what it reached for.

The same instrument has a second use. Where you are weighing options rather than
hunting for holes, what an unsteered agent *builds* is a reading on which shape
the domain treats as natural — see **Consulting it on a fork**, below.

## Why this needs vibe mode

The check that would prevent the mistake also destroys the signal.

An agent that greps the catalogue before writing an example finds the real names,
writes a correct example, and the hole stays invisible forever. An agent that
does not look writes the name its priors say should be there. Only the second
one tells you anything.

So the instrument is a deliberately unverified agent, and the discipline moves
from the agent to you: it is allowed to be wrong, and you are the one who checks.

## The instrument

Agents spawned here are **specimens, not contributors.** Nothing they write is
kept. Say so in the prompt, run them with `isolation: "worktree"` so their edits
are contained, and never merge their branch.

Run three to five at once on *different* tasks. Independent reaches at the same
vocabulary are what turn a single guess into a repetition count, and the count is
most of the signal.

## Protocol

**1. Fix the ground truth first.** Before spawning anything, capture the closed
set of names the project owns, as a list you can diff against mechanically.
Module type ids, exported symbols, CLI flags, config keys, error codes — whatever
the project's vocabulary actually is. Doing this first keeps the check honest.

**2. Give it something to build.** A vibe coder does not design. It starts
editing files in the first minute, and that is exactly what you want: the names
fall out of the build without anyone stopping to consider them. Ranked by yield:

| Task | Why |
|---|---|
| Build a feature that has to reach across the project | Best. Every module, API and flag it calls is a reach, and so is every comment, doc and commit message it writes on the way past |
| Port, extend or wire up something that already exists | Good. Forces contact with the vocabulary at its edges |
| Write the tutorial, the cookbook, ten worked examples | Secondary. Rich in calling conventions, but it is not what a vibe agent does naturally — use it for a second specimen, not the first |
| Ask it to design, plan, propose, or list what is missing | **Never.** It will deliberate, and deliberation is the thing you removed. Asking the instrument what it thinks gets you a considered opinion, which is a worse and completely different artifact. The method only works by accident — and that holds when you are weighing a fork too, which is why **Consulting it on a fork** below still hands it a task rather than a question |

**3. Spawn, in the background.** See the prompt template below. Do not block on
them; they report back on their own.

**4. Harvest** every identifier of the right shape from both their prose and their
worktree diff — see `references/harvesting.md`.

**5. Rank** the misses by how many independent agents reached for each one.

**6. Report** — see the rules below.

## The prompt

```
Build <feature> in <project>, at <path>.

Work in vibe mode: invoke the `vibe-mode` skill now and follow it. Delete the
plan step — your next tool call edits a real file. Ship it end to end: the code,
the comments, the docs and examples that go with it.

One rule on top of it: DO NOT LOOK UP THE VOCABULARY. Do not open
<ground-truth files>, do not grep for <name pattern>. When you need to name a
<thing>, take the first name that comes and keep moving.

For context read only what a user would see: <README, site/, docs/>.

This work is a specimen and will be thrown away. Do not commit, do not merge,
do not touch main, do not fix anything you notice in passing.
```

Ask for the feature, never for the finding. The moment the prompt says "and tell
me what you think is missing," the agent stops being an instrument and starts
being a second opinion.

The denial in paragraph two is the experiment. Without it vibe mode does the
right thing — its own text says *"you need more contact with the real code, not
more thinking. Go open a file"* — and the instrument reads zero.

## Reading the signal

The short version; `references/reading-the-signal.md` has the rest.

- **The name is the signal. The signature is contamination.** A hallucinated
  calling convention is borrowed from whatever system the model learned it in,
  and is usually the one shape your architecture cannot afford.
- **Repetition is strength.** A name reached for by four agents, or reached for
  repeatedly by one as *the* canonical example, is a much louder finding than a
  name that appeared once in a list.
- **Check affordance before calling it a gap.** Some names miss because the
  architecture deliberately forbids them. That is a finding too, but a different
  one: it means the shape is foreign, not that the feature is wanted.

## Reporting rules

The report goes to a human who did not watch the agents run, so:

1. **Say vibe mode was used, and say the findings are hallucinations.** Every
   time. Never present a name an agent invented as though it were a feature that
   exists, and never let a reader mistake the specimen output for a proposal.
2. **Give the count and the quotes.** How many independent agents reached for it,
   and what they wrote, verbatim.
3. **Separate the hole from the shape.** State what is missing, then state
   separately whether the form the agents wrote is one the project could take.
4. **Do not ship the specimens.** The agents' branches are evidence, not work.
   Say where they are and that they are to be deleted.

## Consulting it on a fork

When the question is not "what is missing" but "which of these shapes is right,"
the same instrument answers — and it answers better than asking would.

**Do not ask it which.** An agent handed two options will weigh them, and a
weighed answer is the thing you already have too much of. Instead give several
agents the *same task*, with no mention that there is a fork at all, and read the
choice out of what they built.

| Outcome | What it means | Do |
|---|---|---|
| They converge | That is the shape the domain treats as default | Take it. The deliberation was the expensive route to the same place |
| They split | The domain has no default here | The fork is real and nobody's priors will settle it. Stop looking for an authority and decide on your own constraints |
| Nobody builds the option you were leaning toward | Weak but real | Ask why you were drawn to it. It may be foreign to the domain, or it may be the thing that makes your project worth having |

Two cautions.

**Keep the prompts identical and innocent.** Any hint of the fork in the wording
is the only thing you will end up measuring. If you cannot write the task without
naming the options, this technique does not apply to that question.

**Convergence means conventional, not correct.** It tells you what everything else
does, which is exactly why it is worth knowing and exactly why it is not the
decision. Run the same affordance check as for a hole
(`references/reading-the-signal.md`). Flyback's `blur(radius: 3)` is what every
agent would reach for and still the one form the engine cannot afford.

For a single fast opinion on a design question, `first-instinct` and `gut` are
cheaper and already exist. Reach for this instead when you want the convergence
reading across several independent tries, or when you do not trust yourself to
have asked a neutral question.

## Worked example

Flyback, a patchable video and audio synthesizer. A vibe-mode agent implementing
"switch a module off" wrote `x |> off blur(radius: 3)` as its example — five
times, independently, in the ADR, `docs/language.md`, `site/tutorials.html`, a
code comment, and the assistant's own prompt. It reached for it every time it
needed to name a module you would want to bypass.

`blur` does not exist in Flyback. There is a whole Feedback category with Trails
in it and nothing that softens — a real hole, in a video synth, found for free.

And the caveat in the same finding: the *signature* was wrong. `blur(radius: 3)`
as an inline pipe stage would mean re-evaluating the upstream chain once per tap,
which the per-pixel model cannot afford. The buildable shape was a previous-frame
blur in the Feedback family. Name right, form borrowed.
