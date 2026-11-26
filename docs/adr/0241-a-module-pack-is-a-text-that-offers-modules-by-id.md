# ADR-0241: A module pack is a text that offers modules by id

**Status:** Accepted · 2026-11-26 · *user-directed* · the language pieces of the plan in
`docs/handoff/module-packs.md`, after
[0236](0236-the-text-has-fold-which-carries-a-value-from-pass-to-pass.md)

## Context

A module pack is a `.fbks` whose modules a patch can place by id, so it installs where a
DLL cannot load (iOS, Android, the web, the stores). The text has to say that a file is
a pack, which release of it, and which version of the text it was written in, so a later
Flyback reads an older pack and an older Flyback refuses a newer one instead of
misreading it. It also has to say which of its defs are modules, and what each one's
card says, which a plugin declares in C#.

## Decision

**Two lines open a pack:**

```
pack ada.echoes 1.2.0
language 1
```

- **`pack <id> <version>`.** The id is two or more lower-case words joined by points, the
  shape a plugin's provider id has, so a pack's type ids (`ada.echoes.tape`) cannot
  shadow a built-in's `category.name`. The version is three whole numbers joined by
  points, a token of its own (`TokenKind.Version`); anywhere else it is a malformed
  number that says where a version goes.
- **`language <n>`** names the version of the text, a whole number from 1
  (`PatchLanguage.Version`). A file written for a later one is refused with a message
  rather than half-read. Left out, it is this build's. An ordinary patch may say it too.
- **They open the file.** Either order, each once, before any other line and outside
  every block.

**`module name(...) = body` is a def the pack offers**, as `<pack id>.<name>`:

```
/// A tape echo: what goes in comes back later, quieter each time.
/// category: Effects · words: delay, tape, repeat
/// glyph: M3,3 L27,27
module tape(in = 0, time = 0.25, feedback = 0.5) = { ... }
```

- **It is a def in every other way**: the same parameters, body and result, and the
  pack's own text calls it as one.
- **Only a pack offers one, and only at the top** of the file, outside every block. A
  helper the pack does not offer is a `def`.
- **Its name is lower case**, letters, digits and `_`, so its type id is one.
- **Every parameter has a default**, since every socket a module has does (ADR-0009).
- **The unbroken run of `///` lines above it is its card**: `category:`, `words:` and
  `glyph:` where a line is made only of those, apart by `·`, and every other line its help.
- **`pack`, `language` and `module` are words only where what they head follows**, so
  `let pack = 1` and `let module = 2` still read as names.

What the text reads goes to `LanguageLoad.Pack` and `LanguageLoad.Modules`; the header
changes nothing the file builds.

**A socket's range and a setting's choices follow its default after `in`**:
`travel = 0 in 0..100` turns the socket from 0 to 100, and `steps = 64 in [16, 32, 64]`
makes `steps` a setting rather than a socket, since a choice decides which modules the body
is built from. `in` reads as it does in `for`, and a list as it does anywhere else; `|` was
weighed and set aside, since the text already reads a lone `|` as a mistyped pipe. Each way
a module's choices combine is built when the pack loads, so a mistake on any branch is
found there, and at most 64 ways are allowed. The text picks a setting by name, a number or
a text, as it does a plugin module's.

## Consequences

- It lands dark: nothing reads `Pack` or `Modules` yet, and the language guide and the
  editor's completions leave the words out until a patch can place a pack's module.
- A module's body is checked where it is called, as a def's is. Placing one in a patch,
  which will expand and so check every module, is the next step of the plan.
