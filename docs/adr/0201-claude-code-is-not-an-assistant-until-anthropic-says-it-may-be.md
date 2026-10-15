# ADR-0201: Claude Code is not an assistant until Anthropic says it may be

**Status:** Accepted · 2026-10-15 · *user-directed* · supersedes
[0172](0172-claude-code-is-an-assistant-by-running-the-program-the-person-signed-in.md)

## Context

[0172](0172-claude-code-is-an-assistant-by-running-the-program-the-person-signed-in.md)
ran the `claude` program the person signed in, so their Pro or Max plan paid and
Flyback held no token. Anthropic's
[Claude Code legal and compliance page](https://code.claude.com/docs/en/legal-and-compliance#authentication-and-credential-use)
says it "does not permit third-party developers … to route requests through Free,
Pro, or Max plan credentials on behalf of their users." That binds whoever ships the
plugin, not only whoever uses it, and running the unmodified program does not take
Flyback out of it: the carve-out for signing in to the binary is for a product that
offers Claude Code, under the Commercial Terms, without restricting any of its
sign-in methods. The plugin took the key variables out of the program's environment,
which restricts one.

## Decision

**The Claude Code plugin is removed.** Someone with Anthropic models uses the
OpenAI-compatible plugin against Anthropic's endpoint with an API key. It comes back
only with a written yes from Anthropic for this use.

`src/plugins/Shared/Programs` stays: Codex compiles it in
([0173](0173-codex-is-an-assistant-the-same-way-and-gemini-is-not.md)), and OpenAI
documents plan use by third-party tools.

## Consequences

A saved setting naming the Claude Code assistant reads as an assistant no longer
installed. Nobody on a Claude plan alone has an assistant without a key.
