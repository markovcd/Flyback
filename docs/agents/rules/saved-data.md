# Saved data doesn't break

## What was written stays readable

Patches, bundles, settings, the preset site's databases, the plugin contract and the command-line arguments one version passes another outlive the code that wrote them. A change to the shape of any of them carries its migration in the same commit, with a test that loads the old shape.

**Why:** a user's saved patch is the one thing they cannot regenerate. A format change that breaks it is found by the user, after a release, with no way back.

**When:** from 1.0.0, the first production release. Until then nobody has saved work to lose, so a module may be removed or its sockets reordered without an upgrade step; the preset site's defaults are still migrated, and what the code already does (refusing a newer file, `PatchLoad` listing what is missing, the contract version) stays as it is.

**How to apply:**

- **A patch file's layout is versioned.** Raise `PatchIO.FormatVersion` only when an older reader would get the file wrong: a field renamed, a number counting from somewhere else, a list meaning something new. Adding a module is not a raise. Every raise owes a step in `PatchIO.Upgrade`, each step standing alone, and a fixture of the old layout that a test opens.
- **A file from a newer version is refused whole**, with a message, rather than half-read and saved back in the older layout.
- **Persisted names are forever.** A type id and an opcode's number are never renamed, renumbered or reused once shipped: saved patches and compiled plugins name them. Add a new one instead.
- **Socket order never changes.** Sockets are saved by position, so reordering a module's sockets rewires every saved patch that uses it. A new socket goes at the end.
- **What is missing is reported, not fatal.** A patch that names a module or a plugin this run does not have opens through `PatchLoad` with what is missing listed.
- **Settings load tolerant.** A `*.json` settings file that is missing, empty, damaged or from another version loads as defaults for what it cannot read, and never stops the program starting.
- **Contracts only grow.** The public surface of `Flyback.Core` and `Flyback.Plugins` moves only with `PublicAPI.Unshipped.txt` and the contract version (ADR-0102). The arguments `--apply-update` takes are only ever added to (ADR-0088). A break is a decision the user makes, recorded in an ADR.
- **The preset site's defaults are files** and are migrated by the change that breaks them (the `preset-site-defaults` skill).
- **The preset site's stores migrate on open**, against a database that already has rows: `CREATE TABLE IF NOT EXISTS`, and a column added when it is missing (see `PresetStore`), never a table dropped and remade.
