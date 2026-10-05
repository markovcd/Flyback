# ADR-0173: Codex is an assistant the same way, and Gemini is not

**Status:** Accepted · 2026-10-05 · *user-directed* · follows
[0172](0172-claude-code-is-an-assistant-by-running-the-program-the-person-signed-in.md);
implemented in `src/plugins/Flyback.Plugins.Codex`

## Context

[0172](0172-claude-code-is-an-assistant-by-running-the-program-the-person-signed-in.md)
made Claude Code an assistant that needs no key by running the program the person
signed in. The same was asked of OpenAI's and Google's.

## Decision

**A separate plugin runs the `codex` program the person installed and signed in to
with ChatGPT**, with everything 0172 says: Flyback holds no token, the key variables
(`CODEX_API_KEY`, `OPENAI_API_KEY`) are taken out of its environment, one process per
exchange, the tools offered in words and called in a `<calls>` block, `NeedsKey`
false. It is not part of the OpenAI plugin: that one speaks chat completions to an
address and holds a key; this one speaks to a program and holds nothing.

**Codex is made a plain model as far as it allows, and no further.** `codex exec`
runs with its instructions replaced (`model_instructions_file`), its user
configuration and rules unread, every feature that offers a tool switched off with
`-c features.<name>=false`, search off, skills and project documents off, a
read-only sandbox, and an empty working folder. The switches are `-c` overrides and
not `--disable` flags because an unknown feature name is a hard error to the second
and a warning to the first, so the list can outlive a version.

Two things stay, found by running the program against a local stand-in for the
service and reading the request it sent:

- `apply_patch` and `request_user_input` have no switch; the sandbox is read-only,
  and the instructions say to ignore any tool offered. A call to either is an item
  that is not a message, and is not read.
- The person's global `AGENTS.md` in `CODEX_HOME` is sent whatever is configured,
  since `CODEX_HOME` is also where the sign-in is. The instructions say to ignore it.

Pictures go as files named with `--image`, which attaches them to the one user
message; the prompt says which is which. The model setting `default` passes no model
and leaves the choice to Codex, which knows what the plan offers.

## Gemini is declined

Personal Google sign-in for the Gemini CLI ended on 2026-06-18, so what is left is a
Code Assist licence or a paid key, which is what this is to avoid. Google's terms
call driving the service through third-party software a violation and have said
nothing about running the CLI as a program; the OpenClaw suspensions in 2026 were for
the token. The Gemini plugin stays on a key. Revisit when an individual plan can
sign in to the CLI again and Google says running it is allowed.

## Consequences

Same as 0172: it cannot listen, each exchange pays a process start, and whether
OpenAI's terms keep allowing it is theirs to change, when the plugin is removed and
nothing else moves. A Windows install through npm leaves a `.cmd` shim that is not
run: the program is found as `codex.exe` on the path, in the desktop app's folder or
in the usual install folders. A plan that has used up its Codex allowance answers
with the program's own message, passed on as it was said.
