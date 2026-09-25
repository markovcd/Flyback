# ADR-0134: A plugin declares its modules, and is refused for one it did not

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0102](0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)
for the contract version, [0132](0132-a-plugin-package-says-what-it-is-and-installs-only-when-asked.md)
for the install dialog and [0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md)
for the site

## Context

The install dialog and the shared plugins site read a plugin without running it,
so they could say it adds modules but not which. A plugin builds its module list
in code when `Register` runs. A patch names each module by type id, and Flyback
will later download the plugin a patch is missing, which needs to know which
plugin has a module before installing it.

A list the author writes beside the plugin can disagree with the plugin, which is
why 0132 has no manifest.

## Decision

**A plugin declares each module on its assembly**, with its type id and name:

```csharp
[assembly: FlybackModule(CircleModule.TypeId, "Circle")]
```

An attribute's arguments are compile-time constants, so the list is in the
assembly's metadata and is read without running anything. The contract gains the
attribute.

**The host holds a plugin to it.** After `Register`, a plugin that registered a
module it did not declare, or declared under another name, is refused whole:
everything it registered is taken back, and About says which module. A module
declared and not registered is allowed, for one left out on some systems.

**`pack-plugin` runs the same check first.** It loads the build for the system it
runs on from a copy, in a context it unloads afterwards, and writes no package if
loading it would leave any problem. That runs the author's own code on the author's
machine. A package with no build for that system is checked at its first load.

**The dialog refuses what the host would.** A plugin that adds modules and
declares none has Install off.

**The site lists the declared modules and finds a plugin by one**, with `module`
taking a whole type id.

## Consequences

- The compiler cannot prove the declarations match `Register`; the load does, and
  `pack-plugin` does it before anyone else meets it. Each shipped plugin has a
  test that its declarations are exactly what it registers.
- A declaration repeats the module's name. Renaming a module means renaming it in
  both places, and a miss is a refusal at load rather than a stale list.
- A plugin packed on one system and never loaded there is checked only when a
  user first loads it.
- There is no Gherkin scenario: the specs project reaches the engine, and this is
  the plugin host, the editor, the command line and the site.
