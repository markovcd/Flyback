# ADR-0224: A comment starts with two slashes

**Status:** Accepted · 2026-11-05 · *user-directed* · amends
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

The text language started its comments with `#`, and refused `//` with a message
pointing at it. In a language of notes `#` is also a sharp: `C#4` is a note, `F#`
a pitch in a scale, and a reader has to look twice to tell `C# ...` from a comment.
The lexer never confused the two, since a note is read whole from its letter, but
the text view's highlighter did, coloring the rest of a line as a comment from
the sharp in `C#4`.

## Decision

**A comment starts with `//` and runs to the end of the line.** A `#` that is not a
note's sharp is refused as `hash-comment`, with a message naming `//`, and the rest
of its line is skipped so it raises nothing else. `//` means nothing else in the
language: division is a single `/`.

Arek chose to refuse `#` outright rather than read both: one comment sign, with no
patch carrying the old one quietly.

## Consequences

- Every shipped preset, the language reference, the assistant's handbook, the site's
  snippets and the tests were rewritten with `//`.
- A patch saved before this with a `#` comment no longer reads until its comments are
  changed; that includes shared presets on the preset site. Before 1.0 that is allowed
  (the `saved-data` skill); the message says exactly what to change.
