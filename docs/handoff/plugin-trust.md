# A plugin loads only once somebody said yes, and never holds a key

Planned on 2026-09-28. It is on TODO.md; take it off there, and delete this file, in the
commit that lands it.

## Why

A folder dropped into `plugins/` runs at the next start with no prompt and no check, in
process and with full trust (ADR-0025). Every stored API key is then its for the taking,
three ways:

1. Read the files. `Vault` seals each key with DPAPI `CurrentUser` under the entropy
   `"Flyback.Assist.v1"`, a constant in public source. Five lines of `ProtectedData.Unprotect`
   open every file under `%APPDATA%\Flyback\secrets\`. DPAPI, the Keychain and the Secret
   Service are all per user: anything running as the user opens them.
2. Register an `ISecretStore` with `Priority = int.MaxValue`. `PluginCatalog.PreferredSecretStore`
   takes the highest priority from anywhere, so every key typed from then on goes to the
   plugin's `Keep`.
3. Register an assistant under a shipped assistant's id from a folder named to load first.
   The first registration wins, the real assistant is ignored, and `AssistantPanel` hands
   the impostor the stored key as a string.

No in-process change can close the first route against code that is already running. So
the work has two goals with different ceilings: no code runs that nobody said yes to, and
a plugin that does run never holds the key.

## What

**A. Load only allowed plugins.** The install dialog (ADR-0132) and `flyback-cli plugin allow <folder>`
record `(assembly name, signer key, SHA-256 of the entry assembly)` in the data folder,
never in `plugins/`, since that is the folder an attacker writes. At load, a folder whose
entry assembly hashes to a record, or is signed with the release key, loads; anything else
is `not yet allowed` in `PluginCatalog.Problems` and in `flyback-cli info`, and is not loaded.
The hash is what is checked, so a dll overwritten inside an approved folder drops out with a
reason that says so. An update through the dialog replaces the record; "remove at next start"
deletes it. `flyback-cli plugin deny` and `list` complete the set.

The check follows `PackageSigner.Checked`: a Debug build and the All plugins build beside it
load every folder, exactly as they install unsigned packages today.

**B. Never hand a plugin the key.** The store records `{host, secret}`. An assistant gets an
`IAssistantTransport` that puts the `Authorization` header on requests to the bound host and
no other; the key string never crosses the plugin boundary. An impostor assistant can spend
the user's quota against the real host, which is bounded, visible and revocable, and cannot
learn the key.

**C. Secret stores and assistants from trusted signers only.** `AddSecretStore` is honored
only from the release key, or from an allow record with an explicit `secrets` grant. An id
registered twice refuses both and says so, in place of first-wins, so a hand-copied folder
named `A` cannot shadow a signed plugin.

**D. The key leaves the process.** A later ADR: a `flyback-keys` helper owns the store and
the HTTP send, and the editor asks it over a pipe to send a body to the bound host for an
account. On macOS the Keychain ACL binds to the helper's signature; on Windows an MSIX-packaged
helper gets the same from `PasswordVault`; on Linux the process boundary alone holds. The only
thing that closes the first route. Decide the shape, do not build it with A–C.

Also: the `Vault` entropy comment claims the string stops another program reading the value.
It does not, and the comment goes with A.

## Order

A, then B, then C, one piece of work. A is the change that matters; B and C are what a
plugin the user allowed and should not have can still do afterward.

## The site

Nothing changes. A site plugin already reaches the machine through the install dialog, and
that click is the yes. Review (ADR-0133) reads metadata without running anything and decides
what is listed, not what is safe: the editor must not treat "from the site" as "allowed", or
the site's admin login is the key to every machine. Countersigning published packages so the
dialog can state "listed on the plugin site since June" is a fact worth showing and not a
verdict.

The one flow that gains a step is a folder copied in by hand from a local build, which needs
`flyback-cli plugin allow` once per build in a Release build. The "copied in by hand" paragraph
in `site/plugins.html` gets the command.

## Ruled out

- **A random per-install entropy for `Vault`.** Same-user code reads wherever it is stored;
  obfuscation with an extra file.
- **The marker file beside an installed plugin as the record.** It is in the folder the
  attacker writes.
- **The site as a trust root.** Above.
- **Beating an attacker who can write any file as the user.** They replace `Flyback.exe`.
  The goal is that extracting a zip into `plugins/` is no longer code execution.
- **Signing in with the provider in place of a key.** Checked 2026-09-28: Anthropic forbids
  its OAuth tokens in any product but Claude Code and claude.ai (February 2026); Google cut
  consumer accounts off Gemini's OAuth (June 2026) and calls third-party use a policy
  violation; OpenAI's "Sign in with ChatGPT" (August 2026) is identity only, no model usage.
  All three point third-party apps at API keys, so there is no short-lived token to lean on
  and D stays a decision worth making. The assistant panel should tell the user to make a
  dedicated key with a spend cap, the one bounded thing a person can hand a BYOK app.

## Scenarios

- A folder with no record does not load in a Release build and is listed as not yet allowed.
- The same folder loads in a Debug build.
- `flyback-cli plugin allow` then loads it; overwriting its dll unloads it with the hash reason.
- A shipped plugin, signed with the release key, loads with no record.
- A store registered by an allowed plugin without the `secrets` grant is refused and reported.
- Two plugins registering one assistant id are both refused and reported.
- An assistant's transport sends the header to the bound host and to no other.
