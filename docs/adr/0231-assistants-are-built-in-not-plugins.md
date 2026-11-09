# ADR-0231: Assistants are built in, not plugins

**Status:** Accepted · 2026-11-09 · *user-directed* · supersedes the plugin half of
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md), amends
[0158](0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md) and
[0169](0169-the-hosts-side-of-a-conversation-is-a-project-of-its-own.md)

## Context

Two assistant plugins were left: OpenAi, which speaks chat completions to any
compatible endpoint, a local runtime included, and Gemini, which speaks
generateContent. Claude Code and Codex reach the editor over MCP
([0202](0202-a-coding-assistant-the-person-runs-edits-the-open-patch-over-mcp.md),
[0228](0228-codex-drives-the-editor-over-mcp-and-has-no-plugin.md)), which is not a
plugin. Nobody else had written one, and the two formats cover nearly every
provider. Keeping the door open cost the contract its largest piece: the whole
`Flyback.Plugins.Assist` namespace, `PatchWorkbench` and its tool surface, was public
API every change to the workbench had to answer to, and both plugins compiled a
shared survey loop in as source.

## Decision

**An assistant is code in `Flyback.Assist`, not a plugin.**

- `IPluginRegistry.AddPatchAssistant` is gone, with the assistant half of the
  catalog. `Flyback.Assist` holds `OpenAi/`, `Gemini/` and `Surveys/` beside the
  workbench and the conversation. `Assistants.Shipped` lists the two in the order a
  fresh install prefers them, which replaces each one's `Priority`.
- The editor is handed its assistants through `EditorSetup.Assistants`, empty unless
  the desktop shell passes `Assistants.Shipped`; flyback-cli's `ask` and `probe` take
  them the same way.
- The keyed request kit stays in the contract, because a decision model still sends
  over it. It moves to `Flyback.Plugins.Secrets` and is named for what it is:
  `IKeyedClient`, `KeyedClient`, `KeyCredential`, `KeyedResponse`, `KeyedPost`.
  `DecisionConfig.Transport` is `DecisionConfig.Client`.
- **The contract stays 1.0.0.** Members are removed, which semver would call a major
  change; the user chose to keep the number, since no plugin outside the repository
  used them.

## Consequences

The contract shrinks to what plugins add: modules, presets, sound and MIDI,
secret stores and decision models. The workbench changes without touching
`PublicAPI` files. A third wire format is a folder in `Flyback.Assist` and a line in
`Assistants.Shipped`, landed in a release rather than installed. The plugins
window no longer marks a plugin as the one the assistant sends to. A plugin built
against the old kit names (`IAssistantTransport`, `AssistantCredential`) fails to
load and needs rebuilding.
