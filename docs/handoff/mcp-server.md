# Flyback as an MCP server

Written on 2026-10-15, against `main` at `036ba71`. It is on TODO.md; take it off there, and delete this file, in the commit that lands the last of it. The decision is [ADR-0202](../adr/0202-a-coding-assistant-the-person-runs-edits-the-open-patch-over-mcp.md).

- **Kind:** Plan
- **Status:** Open

## What is wanted

Someone signed in to Claude Code or Codex builds patches with it without an API key, and sees what it does in the assistant's column. Flyback may not start the client itself (ADR-0201), so the client is the person's and Flyback is the server it calls.

## Steps, each a commit that lands whole

1. **`flyback-cli tool`, headless, and a skill.** `flyback-cli tool --list [--json]` prints every `Offered` tool with its schema, and `--briefing` the briefing a model is handed. `flyback-cli tool <name> <patch> --args '<json>'` runs one call on the same workbench `flyback-cli ask` builds, saves the patch, prints the outcome (`--json` for a script; a rendered picture to a file named with `--picture`), and exits non-zero on a refused call. A shipped `SKILL.md` (the briefing's rules plus these commands) lets Claude Code or Codex run the whole assistant flow from a terminal with no server. Gherkin: a scenario adds a module, wires it and reads the patch back.
2. **`flyback-cli mcp <patch>`, headless.** MCP over stdio: `initialize`, `tools/list` and `tools/call` over step 1's dispatch, the briefing as `instructions`. Pictures come back as MCP image content. The patch is saved after each edit. Dark until whole: not in the CLI's help. A Gherkin scenario drives it with canned JSON-RPC lines and reads the patch back.
3. **The `say` tool.** Text the client wants the person to read in the column. Headless, it is written to standard error.
4. **The editor's channel.** A named pipe on Windows and a Unix socket under the user's runtime folder elsewhere, readable only by the user, opened while the setting is on. `flyback-cli mcp` with no patch, or with `--editor`, forwards each call there and answers with what came back. The editor runs a call on the canvas through the same workbench the column uses. Threat-model row in this commit.
5. **The column, driven.** A "Driven by Claude Code" header (the client's name from `initialize`), each call as a step as in a plugin turn, rendered pictures inline, `say` text as the reply. No input box while a client is connected.
6. **Settings and site.** The switch under Settings → Assistant, the setup lines (`claude mcp add …`, Codex's `config.toml`) shown beside it, the site's assistant section and the changelog bullet.

## Open questions

- **Undo.** MCP has no turn end. Group the calls between two `say`s into one edit, or between two pauses longer than a few seconds, or let each be its own edit. Grouping on `say` matches what a plugin turn does and is what I would try first.
- **Hand-written JSON-RPC or the `ModelContextProtocol` package.** The plugins speak their wire formats by hand and the engine takes no dependency (ADR-0019); the CLI could take the package. The protocol subset needed is small, so hand-written fits the house style; a package is a question for the user, not a line to add.
- **Two clients at once.** Refuse the second, or serialize them. Refusing is simpler and says why.
- **Android and the page.** Neither can host a channel a desktop program reaches; this is desktop only.
