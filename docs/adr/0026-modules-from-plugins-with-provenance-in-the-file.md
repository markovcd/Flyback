# ADR-0026: Modules may come from plugins, and the file records which

**Status:** Accepted · 2026-08-12 · *user-directed* · amends
[0025](0025-platform-io-behind-loadable-plugins.md),
[0008](0008-modules-as-data-in-one-catalogue.md) and
[0020](0020-json-patch-files-keyed-by-string-type-ids.md)

## Context

[0025](0025-platform-io-behind-loadable-plugins.md) drew its boundary at
modules, and gave one reason: `.fbk` files are keyed by
[0008](0008-modules-as-data-in-one-catalogue.md)'s type ids
([0020](0020-json-patch-files-keyed-by-string-type-ids.md)), so a module plugin
would make whether a patch opens depend on what happens to be installed.

That reason is weaker than it looks, because the harm in it is not the
dependency — it is that the dependency would be *silent*. A patch opening with
holes where its modules used to be, compiling to something that is not what was
saved, is a bad outcome. A patch that refuses to open and names the plugin it
wants is an ordinary one; every tool with a plugin ecosystem works this way.

So the decision turns on whether the file can say what it needs. It can.

## Decision

**Modules may be contributed by plugins**, through the same registry
[0025](0025-platform-io-behind-loadable-plugins.md) established:

```csharp
void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules);
```

**A provider's module type ids must begin with its id and a dot.** One rule,
three jobs: shadowing a built-in becomes impossible, so does a collision between
two plugins, and the provider of a module can be read off a saved file by
someone who does not have the plugin that defines it.

**`ModuleCatalog` is immutable.** Adding a provider yields a new catalogue and
reports what it refused, rather than mutating one that a compiled program may
already have been built against. `NodeCatalog` keeps the engine's own modules and
gains `Current`, installed once at startup before there is a window. Compilation
and file I/O both take an optional explicit catalogue, so nothing but the shell
needs the installed one.

**A patch records the plugins it needs, as it is written:**

```json
{
  "Requires": [ { "Id": "flyback.sample", "Name": "Sample modules" } ],
  "Nodes": [ … ]
}
```

The display name is stored rather than looked up, because the whole point is to
say something useful about a plugin that is not there to be asked.

**Opening checks two things,** not one. `Requires` is what the file claims about
itself; the module ids are what it actually contains. A file that was
hand-edited, or written before a module was renamed, is short of a module without
being short of any plugin it ever named — so both are reported, and the editor
refuses the patch and leaves the open one alone.

## Consequences

[0008](0008-modules-as-data-in-one-catalogue.md) still holds where it counts: a
module is still data, still one entry pairing sockets with the ops it lowers to,
and a plugin writes exactly what `NodeCatalog` writes. What changed is only that
the catalogue is composed rather than a single array — the shape of an entry, and
the fact that nothing links a module's declared ports to what its emit function
indexes, are both unchanged.

[0020](0020-json-patch-files-keyed-by-string-type-ids.md) gains a header. Old
files load unchanged, and a patch that uses nothing but built-in modules is
written byte-for-byte as it was before, because `Requires` is omitted rather than
written empty.

**`NodeCatalog.Current` is global mutable state**, which nothing else in the
engine has. It is set once, before the first window, and everything added here
takes a catalogue explicitly instead — which is why the tests can compile against
a catalogue containing a plugin's modules without installing anything. If a
second entry point ever needs a different catalogue, that is already possible;
what is not possible is changing it while a program is running, and that is
deliberate.

**Sharing a patch now sometimes means sharing a plugin.** That cost is real and
it is the one 0025 declined to take. It is bounded by being visible: the file
says what it needs, the status bar says what is installed, and a patch built only
from the modules that ship in the engine never acquires the dependency at all.

**Presence is checked; versions are not.** A plugin that is installed but has
changed — a module renamed, ports reordered — is not detected, because a
requirement records an id and a name and not a version. Reordered ports degrade
the way they already do for built-ins, with
[0009](0009-editable-defaults-on-every-input.md)'s defaults filling anything the
saved values no longer cover. A renamed module surfaces as an unknown module,
which is the loud failure this ADR is about. Real version compatibility would
need a policy about what a plugin may change, and there is no evidence yet about
what that policy should be.

There is still no unloading ([0025](0025-platform-io-behind-loadable-plugins.md)),
so installing a plugin means restarting. For modules that is more visible than it
is for a sound device, because the palette is where you would go looking.
