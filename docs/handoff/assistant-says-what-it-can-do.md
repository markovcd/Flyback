# The assistant says what it can do, plugins included

Written on 2026-10-07, on `main` at `7c30bca0`. It is on TODO.md; take it off there, and delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, not started

## What is wanted

Asked what it can do, the assistant answers from what is true on this machine, and plugins are part of the answer:

- **A .NET SDK is installed:** it says it can write a plugin in C# (a module, a field, a source), build it and pack it with `flyback-cli pack-plugin`, and offers to.
- **No SDK is installed:** it says it can do that once one is, and gives the link: <https://dotnet.microsoft.com/download>. It does not offer to start.

The SDK is the one Flyback builds on (.NET 10); an older one does not count, and the answer names the version needed.

## Shape

- The host finds the SDK (`dotnet --list-sdks`, once per session, cached) and states the result in the assistant's briefing, so the model never guesses. It is a fact about the machine, not something the model probes for.
- The briefing's capability summary lists plugin authoring beside patches, listening and rendering, worded by the SDK's presence.
- The plugin contract the assistant writes against is [docs/plugin-guide.md](../plugin-guide.md); the briefing points to it rather than copying it.
- Drivable by an agent: the SDK check is a field in `flyback-cli ask --json`'s context line, so a script can see what the model was told.

## Open questions

- Whether authoring needs its own tool (`write_plugin`) or the assistant uses the file tools it already has; and where it may write, given a plugin loads only once somebody allows it (ADR-0158). A plugin it builds is never loaded without that yes.
- Web editor and viewer have no SDK and no folder to build in; the answer there says authoring is a desktop thing.

Nothing here was run; it is from reading `src/Flyback.Plugins/Assist/` and the README's `pack-plugin` section.
