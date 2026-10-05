# ADR-0172: Claude Code is an assistant by running the program the person signed in

**Status:** Accepted · 2026-10-05 · *user-directed* · follows
[0158](0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md),
which declined signing in with a provider; implemented in
`src/plugins/Flyback.Plugins.ClaudeCode`

## Context

Every assistant took an API key. Someone with a Claude subscription had to make a
key as well and pay twice. [0158](0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md)
checked signing in with the provider instead and declined it: Anthropic allows its
OAuth tokens only inside Claude Code and claude.ai, so Flyback holding one would be
a third-party app using a subscription's token.

## Decision

**A plugin runs the `claude` program the person installed and signed in, and holds
nothing.** Flyback never sees a token: the program signs itself in and the plan pays.
It is the person running Claude Code, not Flyback signing in as them.

**The program is asked as a plain model.** Its tools, settings, skills and servers
are off, it runs in an empty folder, and the key variables are taken out of its
environment so a key can never be billed in the plan's place. Each exchange is one
process, sent the whole conversation as one line of `stream-json` on standard input
(a briefing outgrows a command line); the briefing leads and every earlier turn is
the same bytes, so the prompt cache serves it.

**The workbench's tools are offered in words and called in a `<calls>` block at the
end of a reply.** The program's own tool loop is not used, so the host runs the turn
(0161) and each edit still reaches the window as it is made, with pictures going back
as image blocks. A block that will not read is answered with why, not with a
workbench refusal.

**`IPatchAssistant.NeedsKey` is false for it** (defaulted true, so the contract only
grows). The window draws no key box, `flyback-cli probe` does not ask for one, and
`Unavailable` is whether `claude` is installed. It sends nowhere, so it has no
endpoint for a key to be bound to.

**The program is found on the path and the usual install folders and cannot be
named in a setting:** a settings file that names a program is a settings file that
runs code. A model name is checked against a short pattern before it reaches the
command line, since it is the one setting that does.

## Consequences

It cannot listen: the program takes no sound. It looks at frames. Each exchange pays
the process start, so a turn is slower than over HTTP. Whether Anthropic's terms keep
permitting a program to drive the CLI this way is theirs to change; if they do, the
plugin is removed and nothing else moves.
