# ADR-0115: The assistant may read the presets

**Status:** Accepted · 2026-09-20 · *user-directed* · extends the budget in
[0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md)

## Context

The briefing told the assistant every module and two short examples, and nothing
else. Everything the presets know — that a Mix fed from its own output is a
lowpass, that a String tuned under the keyboard is a reverb, how a picture is tied
to a tune — was written down in the box and unreadable from inside a conversation.
A model asked for a filter reinvented one, where the answer was a preset away.

The patch on the bench was the one exception: open a preset, ask for something
like it, and `describe_patch` reads it. That is one preset, chosen by hand before
the conversation started.

## Decision

**The briefing ends with the presets, a line each, and `describe_preset` gives one
in the language.** Name and description in the list; the whole patch, printed the
way `describe_patch` prints the bench, when one is asked for. The blank presets
are left out, having nothing in them to read.

**The list is budgeted the way the modules are.** A fixed reserve comes off the
briefing's budget before the modules divide the rest; every preset is named, a
description is kept while there is room, and one left out is said so and is still
there for `describe_preset`. The reserve is fixed rather than measured so that
which modules lose their descriptions stays a question about the catalogue alone,
which is what lets the canvas mark them without a conversation to ask.

**A preset somebody saved is one of them**, asked for as each conversation starts
so that one saved since the last is in it.

**The presets go last** so that a preset saved between two conversations leaves
the conventions and the catalogue byte-for-byte identical, and the prompt cache
keeps them.

## Consequences

**The list is cheap and the reading is not.** Nineteen shipped presets cost 2,220
characters of a 100,000 budget. Whole band prints to 11,386, which is what asking
for it costs; every other preset is under a kilobyte.

**It can be read as something to copy.** The preamble says to take the idea and
build what was asked for rather than hand a preset back, which is a sentence in a
briefing and not a guarantee.
