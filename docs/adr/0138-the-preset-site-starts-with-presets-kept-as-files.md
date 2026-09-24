# ADR-0138: The preset site starts with presets kept as files

**Status:** Accepted · 2026-09-23 · *user-directed* · builds on
[0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md)

## Context

Every preset in the box is C# that builds a patch, so it is compiled against the
modules it uses and cannot fall out of step with them. A piece made as a patch
rather than written as code, with samples in it, has no place there: a preset
from the box has no folder to measure a sample's path from. Tranquility is the
first, a psytrance track whose bundle carries two clips of Apollo 11.

## Decision

**The preset site ships a `Defaults` folder and seeds from it as it starts.**
Each `.fbk` or `.fbkb` there is read as a submission is, so its name comes from the
file and its author, description and tags from the patch, and is added to the shelf
once. The site records which file each came from and a hash of it: the same file
again changes nothing, a changed file replaces the stored one and what it says about
itself under the same id, so ratings and downloads survive, and a default the admin
deleted stays deleted. `Site:Defaults` points the site at another folder.

**A file that is not a patch stops the site starting.** It is a broken build, as a
bad setting is, and a shelf quietly short of it would not say so.

**A default is migrated in the commit that breaks it.** A file does not follow the
code the way a preset written in C# does, and this repository removes and renames
modules without keeping the old ones readable. `PresetSiteDefaultsTests` opens every
default against the shipped plugins and fails while one is written in an older
layout, names a module or plugin that is gone, names a file it does not carry, or
has anything to say when compiled. The change that makes it fail rewrites the file.

## Consequences

- A replaced default keeps its rendered media. A migration sounds and looks the same,
  so that is right; a default changed to sound different needs its `{id}.done` marker
  deleted on the media share to be rendered again.
- A default renamed by the admin keeps the admin's name when its file is replaced.
- Tranquility is not in the app's preset gallery as a class. It is reached the way
  any shared preset is, from the gallery's preset site section or the site itself.
