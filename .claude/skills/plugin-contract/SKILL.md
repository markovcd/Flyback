---
name: plugin-contract
description: Use when adding, changing or removing anything public in Flyback.Core or Flyback.Plugins, editing PublicAPI*.txt, or meeting RS0016 or ContractSurfaceTests - public is only what a plugin may name.
---

# The plugin contract

## Public is what a plugin may name

`Flyback.Core` and `Flyback.Plugins` are what a plugin is compiled against (ADR-0102), so every public type and member in them is a promise a release has to keep. The host and its tests see both projects' internals through `InternalsVisibleTo`, so nothing the host needs has to be public. A member only the host reads is `internal`.

**Why:** the contract grew by a third in one release with members no plugin named: the analyzer complained, the complaint was answered by adding the line, and the host's convenience became a plugin's promise. Whatever a plugin can name is something a release has to keep.

**How to apply:**

- The fix for RS0016 (a public symbol not in `PublicAPI.Unshipped.txt`) is `internal`, unless a plugin needs the member. Then the new line is a decision: name it, and the plugin that needs it, in the reply and in the commit message.
- `ContractSurfaceTests` fails on a public type, or a public member, no plugin built here names. A type kept for plugins elsewhere goes in its `Promised` list, a member in `PromisedMembers`, each with a reason that holds on its own; "a plugin might" is not one. An entry leaves the list the moment a plugin names it. What a plugin implements or overrides, a positional record's own shape, and the properties of a type listed in `SavedData` count as named.
- A member is `internal`, not `public`, when its callers are the Editor, the Engine, the Ui, the CLI or a test. The in-box plugins under `src/plugins/` and the two test plugins under `tests/` are the plugins; a type they do not name, no third party has either.
- Extras and field kinds (`NodeExtra`, `FileExtra`, `ExtraField` and everything built on them: `ScaleExtra`, `SampleExtra`, `MidiExtra` and the rest) are how a plugin's module declares an input that is not a knob, which is what makes a plugin useful. They stay public with all their members even when no plugin built here declares one; `ContractSurfaceTests` counts them as promised and fails if one goes internal.
- A member leaves the contract through a `*REMOVED*` line in `PublicAPI.Unshipped.txt`, never by editing `PublicAPI.Shipped.txt`, so the release diff shows what went.
- A `JsonSerializer` writes only public members: a type `PatchIO` or a settings file saves stays public however few plugins name it, and is promised as saved data.
