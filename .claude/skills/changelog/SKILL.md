---
name: changelog
description: Use when adding to or editing CHANGELOG.md - what counts as user-facing, which heading a bullet goes under, and why a feature gets exactly one terse bullet.
---

# CHANGELOG.md

## User-facing only

Entries cover only changes a user of Flyback would notice. Leave out code-comment trimming, README changes, added ADRs, tests and "(ADR-00xx)" references on bullets. When filling out a release section, skip commits that only touch comments, `docs/adr`, the README or tests, and do not write an Internals section made of them.

## One terse bullet per feature

A feature gets **one** bullet, naming what it is and nothing more. No second bullet for its settings, its fallback, its command-line flag or its caveats, and no sentence explaining how or why it works; that belongs in the ADR and the code. The user reads the changelog to see what changed, not to learn the feature.

Draft the section, then collapse it: if two bullets are about the same feature, they are one bullet. Cut clauses starting "which", "so that", "because". Keep numbers only where the number *is* the news. A batch of small related changes is one feature, so one bullet; a bullet per preset is only for the showcases, which are news on their own. An earlier seven-bullet "Recording and export" section and an eleven-bullet preset sweep were each cut back by the user to one.

## Written so a release needs no grooming

A bullet lands in `## Unreleased` already in its released shape; `/release` only renames the heading.

- **Read Unreleased first.** If the feature already has a bullet, edit that one rather than adding another.
- **File it under its heading**, in this order: Modules, Presets, Assistant, Editor, Viewer, Command line, Installing, Performance, Fixes. Add a missing heading in its place in that order; never put a bullet above the first heading.
- **Name what changed, not why.** No "(ADR-00xx)", and no clause starting "which", "so that" or "because".

`ChangelogTests` fails the gate on a bullet outside a heading, a heading off the list or out of order, an ADR or one of those clauses. Collapsing two bullets about one feature is yours: no test sees it.
