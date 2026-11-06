# ADR-0228: Codex drives the editor over MCP and has no plugin

**Status:** Accepted · 2026-11-06 · *user-directed* · supersedes
[0173](0173-codex-is-an-assistant-the-same-way-and-gemini-is-not.md)

## Context

[0173](0173-codex-is-an-assistant-the-same-way-and-gemini-is-not.md) made Codex an
assistant in the column by running the `codex` program the person signed in to.
[0202](0202-a-coding-assistant-the-person-runs-edits-the-open-patch-over-mcp.md)
said it keeps its plugin until the person's own client can edit the open patch over
MCP; once it can, the plugin is a second door to the same thing. It can:
`flyback-cli mcp --editor` serves the editor's tools, edits wait for `propose`, and
Codex adds it through `[mcp_servers]` in its `config.toml`.

## Decision

**The Codex plugin is removed.** Someone on a ChatGPT plan points their own Codex at
`flyback-cli mcp --editor`; the setup lines are in Settings, Assistant. With it goes
`src/plugins/Shared/Programs`, which nothing else compiled in.

## Consequences

A saved setting naming the Codex assistant reads as an assistant no longer
installed. The column's own assistants all hold a key; a plan with no key reaches
Flyback only through an MCP client, desktop only. Android loses nothing, since a
plugin that runs a program could not run there.
