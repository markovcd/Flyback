# Flyback

Flyback is a patchable synthesiser for .NET 10: one module graph generates both a picture and a sound. See [README.md](README.md) for the build and run commands, [docs/engineering-guide.md](docs/engineering-guide.md) for the architecture, the code style and how tests are written, [docs/glossary.md](docs/glossary.md) for the one word each thing is called by, [docs/threat-model.md](docs/threat-model.md) for where outside input enters and what checks it, and `docs/adr/` for the decisions behind the design. [TODO.md](TODO.md) lists work asked for and not yet started; add to it when the user says "add to the todo", and take an item off in the commit that lands it. A plan, an audit or an out-of-scope problem too long for a line is written up in [docs/handoff/](docs/handoff/README.md) and linked from its item.

Standing rules for working in this repo are in `docs/agents/rules/`. They apply to nearly every task, not one kind of it, so read all of them before starting; Claude Code imports them through the paths below:

- @docs/agents/rules/git-workflow.md — commit straight to `main`, and isolate from other sessions' uncommitted work.
- @docs/agents/rules/one-type-per-file.md — one top-level type per file, named for it, and never a type cut into partial files; split a file that holds several, or a type spread over several, whenever a change touches it.
- @docs/agents/rules/prose-style.md — American spelling; succinct comments that never narrate history.
- @docs/agents/rules/terminology.md — when the user says a word the glossary rules out (*agent* for the assistant), correct it in one line at the top of the reply.
- @docs/agents/rules/agent-drivability.md — drivability by an agent comes first; a hack needed to get something done is a feature to propose.
- @docs/agents/rules/propose-refactors.md — code that has gone untidy or ballooned, or one feature's logic written twice, is a refactor to propose, after the task and outside its commit.
- @docs/agents/rules/honest-tests.md — a failing test is fixed by fixing the code, never by weakening, skipping or special-casing the test.
- @docs/agents/rules/security.md — keys stay out of everything; a check is never loosened to make something work.
- @docs/agents/rules/ci-cd.md — land small and often, a red `main` is fixed before anything else, and `main` is always releasable.

Rules that bind only one kind of change are skills in `.claude/skills/`, loaded when their description matches: `pipeline` (workflows, the Dockerfile, a release), `plugin-contract` (public API in Core and Plugins), `saved-data` (anything saved or passed between versions) and `outside-input` (code that reads input from outside). They are as binding as the rules above. Every other skill there is know-how for one task, and its name and description are visible every session.

Commands in `.claude/commands/` (`/bughunt`, `/mutate`, `/burn`, `/release`, `/review-plugin`) are run by name rather than matched. `vibe-check` lives in the [vibe-mode kit](https://github.com/markovcd/vibe-mode) and is installed under `~/.claude/skills/` on this machine.

`.claude/settings.json` turns on the `Flyback Vibe` output style (`.claude/output-styles/vibe.md`) for every session in this repo. It governs wording only: what gets checked, weighed and recommended is unchanged.
