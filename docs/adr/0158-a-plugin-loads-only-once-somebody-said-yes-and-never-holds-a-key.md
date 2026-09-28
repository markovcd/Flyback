# ADR-0158: A plugin loads only once somebody said yes, and never holds a key

**Status:** Accepted · 2026-09-29 · *user-directed* · implemented in
`PluginTrust.cs`, `PluginAllowances.cs`, `ShippedList.cs`, `PluginFiles.cs`,
`KeyedTransport.cs`, `Credentials.cs`, `PluginCommand.cs` and
`Directory.Build.targets` · amends [0025](0025-platform-io-behind-loadable-plugins.md),
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) and
[0132](0132-a-plugin-package-says-what-it-is-and-installs-only-when-asked.md)

## Context

A folder dropped into `plugins/` ran at the next start, in process and with full
trust. Every stored API key was then its for the taking, three ways:

1. Read the files. DPAPI, the Keychain and the Secret Service are per user, and
   the entropy `Vault` mixes in is in public source.
2. Register a secret store with the highest priority, and be handed every key
   typed from then on.
3. Register an assistant under a shipped assistant's id from a folder that loads
   first, and be handed that assistant's key as a string.

Nothing in process closes the first route against code that is already running.
So there are two goals with different ceilings: no code runs that nobody said yes
to, and a plugin that does run never holds a key.

## Decision

**A folder loads only if Flyback shipped it or somebody allowed it, and only while
its files are as they were.** "As they were" is the SHA-256 of every file in the
folder, not the entry assembly alone, since a dependency beside it runs as much as
it does. A folder that fails is not loaded at all and is a plugin problem saying
why and what to run.

- *Shipped*: the build writes `plugins.sha256` beside `plugins/`, listing every file
  of every plugin it laid out. It is never in `plugins/`, and a release carries it
  under the signed SHA256SUMS.
- *Allowed*: `allowed-plugins.json` in the data folder, keyed by the folder's full
  path, holding its files, the key its package was signed with, and whether it may
  keep keys. The install dialog writes one for what it unpacked; removing the plugin
  deletes it at once. `flyback-cli plugin allow|deny|list` does the same for a
  folder copied in by hand, and `list --json` says which folders load and why.
- *Adopted*: the first checked start over a plugins folder allows every folder a
  package installed there, since each was installed with a yes before there was a
  list to keep it in. Once per plugins folder, recorded, so a folder that arrives
  later is never adopted.

The check follows `PackageSigner.Checked`: a Debug build and the All plugins build
load every folder, as they install unsigned packages.

**An assistant is handed an `IAssistantTransport`, never the key.** The host puts
the key on requests to one origin (`scheme://host[:port]`) and on no other, in the
header the assistant's credential names; redirects are not followed, since a header
of a provider's own naming would follow one to another host. A key entered is bound
to the origin the assistant said it would send to (`IPatchAssistant.Endpoint`) and
kept in the store with it, so an endpoint pointed elsewhere afterwards sends it
nowhere new: the panel says so and asks for the key again. A key kept before keys
had an origin is bound to where it is next sent, and kept again that way. An
environment variable is bound to wherever the assistant sends now; any code in the
process can read one anyway. `AssistantConfig.ApiKey` is gone, which moves the
contract's major.

**A secret store registers only from a shipped plugin or one allowed with
`--secrets`, and an id registered twice is refused to both.** That covers sound
backends, MIDI inputs, assistants and secret stores alike: no folder can stand in
for a plugin it shares an id with. A plugin's own id stays first-wins.

## Consequences

Extracting a zip into `plugins/` is no longer code execution. Somebody building a
plugin runs `flyback-cli plugin allow` once per build in a Release build of Flyback,
or runs a Debug one. A plugin the user allowed and should not have can still spend
the user's quota against the host a key is bound to, which is bounded, visible and
revocable; it cannot learn the key. The assistant panel asks for a key made for
Flyback alone, with a spending limit.

The preset site changes nothing: its install dialog click is the yes. Review decides
what is listed, not what is safe, so "from the site" is never taken as "allowed";
otherwise the site's admin login would be the key to every machine.

## What is left: the key leaves the process

Only a key the process never holds is safe from code already running in it. The
shape, for a later decision: a `flyback-keys` helper owns the store and the HTTP
send, and the editor asks it over a pipe to send a body to the bound origin for an
account. On macOS the Keychain's access list binds to the helper's signature; on
Windows an MSIX-packaged helper gets the same from `PasswordVault`; on Linux the
process boundary alone holds.

## Ruled out

- **Signing shipped plugins with the release key.** Every Release build would need
  the private key, the gate and a contributor's build included, or load no plugins.
  The list the build writes is trusted as far as the executable beside it is, which
  is as far as anything here can be.
- **The marker file beside an installed plugin as the record.** It is in the folder
  the attacker writes.
- **A random per-install entropy for `Vault`.** Same-user code reads it wherever it
  is kept.
- **The site as a trust root.** Above.
- **Beating an attacker who can write any file as the user.** They replace
  `Flyback.exe`.
- **Signing in with the provider in place of a key.** Checked 2026-09-28: Anthropic
  allows its OAuth tokens only in Claude Code and claude.ai; Google cut consumer
  accounts off Gemini's OAuth; OpenAI's sign-in is identity only. All three point
  third-party apps at API keys.
