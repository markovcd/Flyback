# Flyback as an MCP server

Written on 2026-10-15, against `main` at `036ba71`. It is on TODO.md; take it off there, and delete this file, in the commit that lands the last of it. The decision is [ADR-0202](../adr/0202-a-coding-assistant-the-person-runs-edits-the-open-patch-over-mcp.md).

- **Kind:** Plan
- **Status:** Open

## What is wanted

Someone signed in to Claude Code or Codex builds patches with it without an API key, and sees what it does in the assistant's column. Flyback may not start the client itself (ADR-0201), so the client is the person's and Flyback is the server it calls.

## Steps, each a commit that lands whole

1. **Done: `flyback-cli tool`, headless, and a skill.** `ToolCommand` runs one call on the workbench over the patch file and keeps each edit in `ToolHistory` (`<data folder>/tools`), with the workbench's own patch JSON and handles, so a `.fbks` read back with new ids still answers to the handles the client chose. `tool --skill` prints the shipped `src/Flyback.Cli/Skill/SKILL.md`; `tool --list --json` already gives each tool's `inputSchema` as MCP wants it. Undoing every edit to a file that did not exist removes the file. The workbench is built with vision and no hearing, so `listen` is not listed, though it still runs if called.
2. **Done: `flyback-cli mcp <patch>`, headless.** `McpServer` answers `initialize`, `ping`, `tools/list` and `tools/call` as one JSON-RPC message a line, by hand rather than through the `ModelContextProtocol` package; each call goes through `ToolCommand.Dispatch`, so the patch is saved and kept in step 1's history after every edit. The briefing is the `instructions`, a render comes back as `image` content, a refused call as `isError`. Hidden from the CLI's help until step 6. Try it with `claude mcp add flyback -- flyback-cli mcp /path/to/patch.fbks`.
3. **Done: the `say` tool.** `McpServer` lists it after the workbench's tools and answers it itself; headless, the text goes to standard error. Step 4 sends it to the column instead.
4. **Done: the editor's channel.** `EditorChannel` (in `Flyback.Host/Driving`) is a `CurrentUserOnly` named pipe on Windows and a socket at `$XDG_RUNTIME_DIR/flyback-editor.sock` elsewhere, one JSON message a line both ways, lines capped at 32 MB. `DrivenEditor` opens it at start while `driven.letProgramsEdit` is on in the settings file, with no switch in the window until step 6; one program at a time, a second told so. `flyback-cli mcp --editor`, or with no patch, sends each call through `EditorTarget`, which connects at the first call and again after the editor closes. `DrivenBench` keeps one workbench while the canvas is as its last call left it, and takes up a hand edit keeping the handles; each edit is one undo step, and `say` goes to the status line. A second editor opened on Unix takes the socket path from the first.
5. **The column, driven.** A "Driven by Claude Code" header (the client's name from `initialize`), each call as a step as in a plugin turn, rendered pictures inline, `say` text as the reply. No input box while a client is connected.
6. **Settings and site.** The switch under Settings → Assistant, the setup lines (`claude mcp add …`, Codex's `config.toml`) shown beside it, the site's assistant section and the changelog bullet.

## Open questions

- **Undo in the editor.** Headless, each call is one step in the history on disk (step 1). In the editor, MCP has no turn end. Group the calls between two `say`s into one edit, or between two pauses longer than a few seconds, or let each be its own edit. Grouping on `say` matches what a plugin turn does and is what I would try first.
- **Hand-written JSON-RPC or the `ModelContextProtocol` package.** The plugins speak their wire formats by hand and the engine takes no dependency (ADR-0019); the CLI could take the package. The protocol subset needed is small, so hand-written fits the house style; a package is a question for the user, not a line to add.
- **Two clients at once.** Refuse the second, or serialize them. Refusing is simpler and says why.
- **Android and the page.** Neither can host a channel a desktop program reaches; this is desktop only.
