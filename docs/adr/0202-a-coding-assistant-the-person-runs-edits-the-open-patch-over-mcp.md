# ADR-0202: A coding assistant the person runs edits the open patch over MCP

**Status:** Accepted · 2026-10-15 · *user-directed* · follows
[0201](0201-claude-code-is-not-an-assistant-until-anthropic-says-it-may-be.md);
plan in [docs/handoff/mcp-server.md](../handoff/mcp-server.md)

## Context

[0201](0201-claude-code-is-not-an-assistant-until-anthropic-says-it-may-be.md)
removed the Claude Code assistant: Flyback may not start `claude` and route its own
requests through a person's plan. Anthropic does allow the person to run Claude Code
themselves, signed in to their own plan, with whatever MCP servers they add. Codex,
and most other coding assistants, take MCP servers the same way.

## Decision

**Flyback is an MCP server; the person's own Claude Code or Codex is the client.**
`flyback-cli mcp` speaks MCP over standard input and output, so it is added with
`claude mcp add flyback -- flyback-cli mcp` or Codex's `[mcp_servers]`. Flyback
never starts the client and never sees its sign-in.

**Its tools are the workbench's.** The server offers `ToolTable` as it stands, each
tool's `PatchTool` spec as the MCP tool, and the briefing as the server's
instructions, so a model is told the same thing over MCP as over a plugin and a new
tool reaches both. The same tools are commands too: `flyback-cli tool <name>` runs one
call on a patch file, and a shipped skill teaches a client to use them, for anyone
who would rather the client ran commands than added a server.

**It edits the open patch when the editor is running, and a file when not.** With
an editor running and the setting on, `flyback-cli mcp` passes each call to it over a
local channel only the same user can open, and the editor runs it on the canvas.
With none, it runs the workbench headless over the patch file it was given, so an
agent can drive and check it with no window.

**The assistant's column shows it.** An editor that is being driven shows each call
as a step as it arrives, the pictures it rendered, and what the client said through
a `say` tool. The column has no input box while it is driven: the conversation is
typed into the client, which is the person's own program.

**Off until switched on.** Settings → Assistant has "Let a program on this computer
edit the open patch", off by default. The editor opens the channel only while it is
on, and the column says when a client is connected.

## Consequences

Someone on a Claude or ChatGPT plan has an assistant again without a key, inside the
terms. The conversation lives in the client, not beside the patch, so it is not
saved with it. A turn has no end Flyback can see, so how edits group for undo is
the plan's open question. The local channel is a new boundary and gets its row in
the threat model in the commit that opens it. Codex keeps its plugin until this
lands; once it has, the plugin is a second door to the same thing.
