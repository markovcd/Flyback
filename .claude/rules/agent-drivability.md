# Agent drivability

## Drivability comes first

When a feature, a command or a fix can be shaped more than one way, pick the one an agent can drive: run it, check it and debug it with no eyes on the window. It outranks convenience of implementation and polish of the window. The engineering guide's [Drivable by an agent](../../docs/engineering-guide.md#drivable-by-an-agent) says what that means in practice: a command before a window, answers a script can read, the editor headless, failures that say where.

**Why:** the user stated it as the project's priority. Nearly every feature here is built, tested and debugged by an agent, so whatever an agent cannot reach is work nobody can check.

**How to apply:** before settling a design, ask how an agent would confirm it works. If the answer is a screenshot, a person, or a throwaway program, the design is not done.

## A hack is a feature request

Reaching for a hack to get something done is the signal that Flyback is missing a feature. Name the friction and propose the feature that would remove it.

Hacks look like:

- a throwaway test or scratch program written only to read a value out of the engine;
- reflection into private state;
- screen coordinates, `SendKeys` or a window capture where a command could answer;
- parsing human-readable output because there is no `--json`;
- sleeping and polling for a state nothing reports;
- hand-editing a patch file or a settings file to reach a state the tools cannot;
- running the same sequence of commands again because no single command does it.

**Why:** the user asked for it outright. A hack solves the task once and leaves the friction for the next session, which pays for it again.

**How to apply:** finish the task with the hack if it is the only way, then end the reply with the proposal: what hurt, in a line, and the command, flag or service that would have made it unnecessary. Do not build the feature unasked; it is a proposal. When the user agrees, add it to `TODO.md`. A proposal that repeats an item already in `TODO.md` or declined by an ADR says so instead.
