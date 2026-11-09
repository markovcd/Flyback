# ADR-0233: Secret stores are built in

**Status:** Accepted · 2026-11-09 · *user-directed* · amends
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) and
[0158](0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md), follows
[0232](0232-decision-models-are-built-in-and-the-keyed-client-leaves-the-contract.md)

## Context

After [0232](0232-decision-models-are-built-in-and-the-keyed-client-leaves-the-contract.md)
the only part of the key's path still in the plugin contract was where it is kept:
`ISecretStore` and `AddSecretStore`. Three plugins filled it, Dpapi, Keychain and
Keyring, one per desktop system, all Flyback's own. A plugin store is handed every key
typed from then on, so [0158](0158-a-plugin-loads-only-once-somebody-said-yes-and-never-holds-a-key.md)
gave it a second gate: only a shipped plugin, or one allowed with
`flyback-cli plugin allow --secrets`, could register one.

## Decision

**The secret stores are code in `Flyback.Assist.Keys`, not plugins.**

- `ISecretStore`, the three stores and `SecretStores.Shipped` live beside the keyed
  client. The editor gets them through `EditorSetup.SecretStores`, empty unless the
  desktop shell hands them over, and flyback-cli through its `Plugins`.
- `AddSecretStore`, `plugin allow --secrets` and the allowance's `secrets` flag go. An
  `allowed-plugins.json` that still says `"secrets": true` is read as if it did not.
- `System.Security.Cryptography.ProtectedData` moves with Dpapi. It compiles anywhere
  and answers only on Windows.
- `GlobalConstants.DataFolder` becomes internal: the stores were the last plugins to read it.
- **The contract stays 1.0.0**, as in 0231 and 0232.

## Consequences

No plugin ever sees a key, so the trust check is one question again: does this folder
load. The contract is what a plugin adds to a patch or to the machine's sound and MIDI:
modules, presets and backends. A new place to keep keys, such as the iOS Keychain, is a
store in `Flyback.Assist.Keys`.
