# ADR-0241: A module pack opens with its id, its version and its text

**Status:** Accepted · 2026-11-26 · *user-directed* · first step of the plan in
`docs/handoff/module-packs.md`, after
[0236](0236-the-text-has-fold-which-carries-a-value-from-pass-to-pass.md)

## Context

A module pack is a `.fbks` whose modules a patch can place by id, so it installs where a
DLL cannot load (iOS, Android, the web, the stores). Before a pack can offer modules, a
file has to be able to say it is one: which pack, which release of it, and which version of
the text it was written in, so a later Flyback can read an older pack and an older Flyback
can refuse a newer one instead of misreading it.

## Decision

**Two lines open a pack:**

```
pack ada.echoes 1.2.0
language 1
```

- **`pack <id> <version>`.** The id is two or more lower-case words joined by points, the
  shape a plugin's provider id has, so a pack's type ids (`ada.echoes.name`) cannot shadow
  a built-in's `category.name`. The version is three whole numbers joined by points, a
  token of its own (`TokenKind.Version`) that the text refuses anywhere else.
- **`language <n>`** names the version of the text, a whole number from 1
  (`PatchLanguage.Version`). A file written for a later one is refused with a message
  rather than half-read. Left out, it is this build's.
- **They open the file.** Either order, each once, before any other line and outside
  every block. Each is a word only where what it heads follows it, so `let pack = 1` and
  `let language = 2` still read as names.
- **The header changes nothing the file builds.** It is read into `LanguageLoad.Pack`;
  a pack with no modules builds the same patch as its text without the header.

## Consequences

- The header lands dark: nothing reads `LanguageLoad.Pack` yet, and the language guide
  and the editor's completions leave it out until a pack can offer a module.
- `language` is also accepted in an ordinary patch, where it only guards against a newer
  text.
