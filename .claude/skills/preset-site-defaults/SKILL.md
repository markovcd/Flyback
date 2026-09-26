---
name: preset-site-defaults
description: Use when a change to the file format, a module, or a plugin would stop one of the preset site's default .fbk/.fbkb files in src/Flyback.Server/Defaults/ from opening or compiling cleanly - migrating those files in the same commit.
---

# The preset site's defaults are files, and are migrated

`src/Flyback.Server/Defaults/` holds the presets the preset site starts with (ADR-0138), as `.fbk` and `.fbkb` files rather than C#. A file does not follow the code the way a preset class does, so any change that stops one opening or compiling cleanly rewrites it in the same commit:

- a change to the file format (`PatchIO.FormatVersion` raised, a field renamed or moved);
- a module removed or renamed, or its sockets reordered, renumbered or given a different meaning;
- a plugin renamed, or a module moved from one plugin to another.

**Why:** the user decided shared showcase pieces live on the preset site as patch files, not in the repository as classes, and said outright that the price is migrating them whenever the format changes. This repository otherwise drops old modules without keeping old files readable, so nothing else would.

**How to apply:** `PresetSiteDefaultsTests` in `Flyback.Plugins.Tests` fails while a default is written in an older layout, names something gone, names a file it does not carry, or has anything to say when compiled. When it fails, open the file with the build from before the change, make the change to the patch, and save it again at the current version; for a bundle, `flyback-cli pack` it again so its files travel with it. A socket that moved without the test noticing still changes what a default sounds like, so after a change to a module a default uses, listen to it (`flyback-viewer --hidden --for 20`) or compare a render against one from before.
