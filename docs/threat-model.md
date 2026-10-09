# Threat model

Where input or code from outside crosses into Flyback, what stands at the door, and where to read why. The rules are in [security.md](agents/rules/security.md); the systems are in the [C4 model](diagrams/README.md). A new boundary is added here in the commit that opens it.

## Assets

- **A user's API key.** Lives in the operating system's store (ADR-0034); never in a file.
- **The release signing key.** Signs `SHA256SUMS` and the preset site's plugins (ADR-0088). Only in the `RELEASE_SIGNING_KEY` secret.
- **A user's patches and takes.** Cannot be regenerated ([saved-data.md](agents/rules/saved-data.md)).
- **The user's machine.** Flyback loads code and opens files other people wrote.
- **The preset site's data.** Presets, plugins, letters and reports in D1 and R2.

## Boundaries

| Boundary | Hostile input | What stands at the door | Read |
|---|---|---|---|
| Opening a patch, bundle or sample | A crafted file: huge lengths, deep nesting, a path in a bundle's name | Readers cap every length against what the input could hold; parsers report and never throw; a bundle is read into memory and writes no path | `PatchIO`, `PatchBundle`, `PatchPaths` in `Flyback.Engine`; ADR-0020 |
| Command-line flags | `--size`, `--seconds`, `--fps` large enough to overflow | A ceiling on each, overflow checked | `flyback-cli`; [security.md](agents/rules/security.md) |
| Loading a plugin | Code that runs with the user's rights | Loads only if shipped or allowed, and only while every file hashes as it did | `PluginTrust`, `PackageSigner`; ADR-0158 |
| Loading an ASIO driver | Native code a sound card's installer registered, run in Flyback's process | Only the driver picked on the Sound tab, from those `HKLM\SOFTWARE\ASIO` lists, which only an administrator writes | `AsioDrivers`; ADR-0192 |
| An assistant plugin | Reads or leaks the key; redirects it to another host | The plugin holds no key; the host puts it on a request for one origin and follows no redirect | ADR-0158 |
| Installing a package | A tampered or unsigned `.fbkp` | Signature verified against the committed key before install | `PluginInstalls`, `PackageSigner`; ADR-0102 |
| An update | A forged or replayed release | One signature over the list that names every package; list checked before download, package before unpack; nothing unverified written outside `.partial` | ADR-0088; `release-key.pem` |
| The assistant's reply | A model that writes a hostile patch or asks for a file outside the patch | A reply is a patch edit, checked like any opened patch | ADR-0033 |
| The preset site's forms | Injection, an oversized upload, one user reaching another's data | Checks at the Worker; parameters, not concatenation; rate limits; uploads unpublished until the admin publishes | `worker/src/checks.ts`, `limits.ts` |
| The admin page | A request that skips Access | The Worker verifies the Access token itself | `worker/src/access.ts` |
| A submitted plugin | A package that runs when reviewed | Decompiled and read, never run | `/review-plugin` |
| CI | A fork's pull request on the self-hosted runner | The runner builds `main` only; workflows pinned to SHAs, least permission; secrets reach Release and Site only | [pipeline.md](agents/rules/pipeline.md) |
| Usage statistics | Identifying a person | Counts what was played and nothing of who; `Usage.cs` is the whole list | ADR-0094 |
| Dependencies | Code nobody here read | The engine takes none; restores are locked to `packages.lock.json` | ADR-0019 |

## Not defended

- **A plugin the user allowed.** It runs with their rights and can read what any code in the process can. A spending limit would cap accidents, not this (see TODO).
- **A user who loads a Debug build.** It loads every folder (ADR-0158).

## Keeping it true

- A change that opens a boundary adds a row; one that moves a defense edits its row.
- The row names where to read the check, not a test count. A defense with no test is a TODO item, not a row.
