# ADR-0169: The host's side of a conversation is a project of its own

**Status:** Accepted · 2026-10-01 · *user-directed* · implemented in `src/Flyback.Assist/`

## Context

`flyback-cli ask` holds the same conversation the editor's assistant column does, and the
two were not to have a copy each. The run (`AssistantRun`), the saved conversation and its
store first moved out of `Flyback.Editor` into the internal half of `Flyback.Plugins`,
the one project both programs reference. That made Plugins hold host code a plugin never
sees, in the assembly a plugin is compiled against, which the user wants kept small. What
a conversation does when it begins, carries on or takes a turn was still written twice,
in the column and in the command.

`Flyback.Engine` cannot hold it: Plugins references Engine, and the run speaks the plugin
contract (`IPatchAssistant`, `PatchWorkbench`, `PatchEvent`).

## Decision

**`Flyback.Assist` holds the host's side of a conversation**, over Core, Engine and Plugins,
and is referenced by `Flyback.Editor` and `Flyback.Cli`. It has `AssistantRun`,
`SavedConversation`, `ConversationStore`, the transcript's `TranscriptLine`, `Voice` and
`Spoken`, and `AssistantSession`.

**`AssistantSession` is the conversation from one message to the next**: it takes a run,
starts its log, tells the transcript why a new one began or that a provider forgot, hands
it the briefing, writes each turn and saves. A host gives it an `ITranscript`: the column's
`TranscriptView`, or the console's. Each host still makes the run and decides when to start
again, since only the editor has a canvas edited underneath it.

**Plugins keeps the contract and what the contract needs**: the workbench, the turn loop
and the transport a provider calls through.

## Consequences

- A conversation the CLI had opens in the editor and carries on, because both save it with
  the same code.
- One more project in the solution, with its own lock file.
- `Credentials`, `AssistantSettings` and `ConversationLog` stay in Plugins' internal half for
  now; `probe` and the server use them, and they can follow when something needs them to.
