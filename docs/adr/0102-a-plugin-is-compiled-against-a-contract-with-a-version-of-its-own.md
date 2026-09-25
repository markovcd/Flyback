# ADR-0102: A plugin is compiled against a contract with a version of its own

**Status:** Accepted · 2026-09-18 · *user-directed* · amends
[0025](0025-platform-io-behind-loadable-plugins.md), which named the two
host-owned assemblies, and takes up the other half of the question
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md) left open

## Context

A plugin is compiled against `Flyback.Core` and `Flyback.Plugins`
([0025](0025-platform-io-behind-loadable-plugins.md)), and the plugins in the box
are rebuilt with everything else, so nothing here has ever met a host it was not
built beside. A plugin somebody else built is different, and
[0088](0088-a-release-installs-itself-at-the-next-start.md) made it common: a
release installs itself, so the host moves on under whatever is in `plugins/`
without anybody deciding that it should.

Three things made that worse than it had to be.

**Everything was the contract.** `Flyback.Core` had some 150 public types, and a
plugin could name any of them: the lexer, the GLSL emitter, the AVI writer.
Whatever a plugin can name is something a release has promised it.

**Every release looked like a change to it.** Each assembly took the release's
version, so the number a plugin was stamped with said when it was built and
nothing about whether what it was built against had moved.

**A break was silent until it was not.** The runtime binds a reference to an
older `Flyback.Core` to the host's newer one without complaint. A member that has
since gone is missed only when the method naming it is first compiled — for a
module that is the middle of compiling somebody's patch, and for a sound backend
it is the audio thread. `PluginHost` catches what `Register` throws and nothing
after it.

[0026](0026-modules-from-plugins-with-provenance-in-the-file.md) declined version
checks between a *patch* and the plugin it needs, for want of a policy about what
a plugin may change. This is the other direction — a *plugin* and the host it
needs — and here the policy can be written, because the host is this repository.

## Decision

**What runs a patch is an assembly no plugin references.** `Flyback.Engine` takes
the compiler and both backends, the text language, the renderers, every file
format, the patch's file I/O, history, clipboard and bundle, the built-in
presets and the values played into a running program. `Flyback.Core` keeps what
a patch and a module are made of: the graph, the sockets, `NodeDef` and its
extras, the `Emitter` a module lowers itself through, the layout and the
built-in catalogue.

The line was found rather than drawn: that half already needed nothing from the
other but the names a Meter listens on, the axis an Analyzer is drawn along and
the record an extra raises a complaint with, and each of those was a few lines
sharing a file with something larger.

**The built-in modules stay in Core**, though they are not an interface. A
plugin's preset names them by id and indexes their ports by the constants beside
them, so what ships in the catalogue is something a plugin depends on whichever
assembly it sits in — and sitting there, it is written against the same emitter
a plugin is given, with nothing else in reach.

**Public is what a plugin could need; the host sees the rest as internal.** Both
contract assemblies give `InternalsVisibleTo` to the host's own assemblies and
their tests, and never to a plugin. So what only the host touches — the program
an emitter hands back, the owners of its state, the preset list, the layout, the
plugin catalog and loader, the stored assistant settings and credentials, a
workbench's construction and saving — is internal, and the host needs nothing
made public to reach it. The test for public is whether a module or plugin could
use it in principle, not whether one does today: a built-in module is written
against the same surface, so what one uses (a picture or a clip read from a file,
a MIDI signal, a meter's level, a spectrum's axis) is public for a plugin's
module too.

**Core keeps its name and its namespaces.** A plugin built before the split names
`Flyback.Core.Graph.NodeDef` in `Flyback.Core`, and that is still where it is. A
namespace says what a type is about and an assembly says who may see it, so
`Flyback.Core.Compile` now spans two: the emitter and the compiler are both about
compiling, and only one is handed to a plugin.

**`Flyback.Plugins` takes the engine as a private reference**, so it does not
flow into the build of whatever references the contract. A plugin that reached
for the engine through it fails to compile. `PluginLoadContext.HostOwned` names
the engine all the same, for the plugin that ships a copy.

**The contract has a version, and it is not the release's.**
`PluginContractVersion` in `Directory.Build.props` is the `AssemblyVersion` of
both contract assemblies and of nothing else.

| Moves | When | What it costs |
|---|---|---|
| major | something a plugin could have named was removed or changed | every plugin built against the last one is refused until rebuilt |
| minor | something was added | a plugin built against the new one is refused by a host that has only the old |

It starts at 1.0.0, above every release so far. A plugin stamped with a release's
0.x is therefore refused, which is the honest reading: before this there was no
promise for it to have been built against.

**The host asks before it runs anything.** `ContractVersion` reads what the
plugin's assembly says it referenced — the compiler wrote it down, so a plugin
declares nothing — and a plugin from another major, or from a later minor, is a
line in `PluginCatalog.Problems` saying which of the two needs replacing.

**The surface is written down, and the build holds the code to it.**
`Microsoft.CodeAnalysis.PublicApiAnalyzers` keeps two files beside each contract
project: `PublicAPI.Shipped.txt` is the surface as the last release offered it,
and `PublicAPI.Unshipped.txt` is what has changed since, with anything taken away
marked `*REMOVED*`. A public member in neither is a build error that names the
line to add. The files record what a signature does not — the number behind each
enum member, the value of each constant, the default of each optional parameter —
because the compiler copies all three into the plugin. So the second file is the
answer to which number moves: the minor with the first line added since a
release, the major with the first `*REMOVED*`. A release moves what is unshipped
into what is shipped.

It is a package in the one project
[0019](0019-no-third-party-dependencies-in-the-engine.md) keeps free of them, and
is let in on two grounds. It is Microsoft's, from the compiler's own repository.
And it runs in the compiler and is gone from what is built: Core still references
nothing outside the BCL, which is what that decision was protecting.

**What may change without a new major:**

- *An interface a plugin calls* — `IPluginRegistry`, and `Emitter`, which is a
  class used as one — gains members freely.
- *An interface a plugin implements* — `IAudioOutput`, `IMidiInput`,
  `ISecretStore`, `IPatchAssistant` — gains a member only with a default body, or
  as a second interface the host looks for with `is`. The file does not say which
  members have a body, so this one is a person's to check.
- *A record's positional parameters are closed.* An optional one added to the end
  is a different constructor, and the old one is gone. What is new is an init
  property, as it has been on `NodeDef` and `EmitContext` since
  [0051](0051-a-quantisers-scale-is-a-set-on-the-node.md).
- *An enum is numbered by hand and only added to at the end of the numbers.*
  `OpCode` is, from here. The shipped modules name its members some three hundred
  times, and an op slipped in among the others would renumber every one after it
  in the host and in no plugin already built.
- *A constant's value is closed*, for the same reason.
- *A base class a plugin derives from* — `NodeExtra` — gains virtual members with
  a body, never abstract ones.
- *Nothing is removed except at a major*, and is marked `[Obsolete]` for the whole
  of the major before.

## Consequences

Core's public surface is some 70 types where it was some 150, and the rest can
change between releases without the question arising. A new type in Core or
`Flyback.Plugins` starts internal, and the analyzer asks for a line only when a
plugin is meant to see it.

A release folder holds `Flyback.Engine.dll` beside the other two, and
`HostOwned` has three names in it. Only two of them are the boundary.

`Flyback.Plugins` is still two things in one assembly: the contract, and the host
and workbench behind it. The host's half is internal, and the workbench's public
members are the ones an assistant calls. It cannot move to the engine, which may
not reference the contract; what can be said is that its surface names no engine
type, and the private reference is what would notice if that stopped being true
— the assistants in the box would stop compiling.

A plugin assembly named after one of the host's could claim those internals,
since nothing here is strong-named. That plugin has stepped outside the contract
and gets no promise from it.

**Shape is checked; behavior is not.** A module that lowers to something
different, a default that means something new, a preset's built-in that gained a
port in the middle: none of these changes a line of either file in a way that
says "breaking", and a person still has to say so.

Only a folder's entry assemblies are asked. A plugin's private dependency that was
itself built against the contract is not, and would fail the old way.

No plugin built against an earlier contract is kept and loaded by the tests,
because there is not one yet: 1.0.0 is the first. The release that ships it is the
first binary worth freezing, and a test that loads it with every method prepared
is what would turn "the surface did not change" into "the old plugin still
runs".

What [0026](0026-modules-from-plugins-with-provenance-in-the-file.md) left open is
still open. A patch records the id and name of a plugin it needs and not a
version, and nothing here changes that.
