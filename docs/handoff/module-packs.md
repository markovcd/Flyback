# Module packs: modules written in the text, for where a DLL cannot load

Planned on 2026-11-16, on `main` at `57aa9ab0`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open. `fold` (ADR-0236), the header and `module` (ADR-0241), and placing a pack's module as one module expanded before compiling (ADR-0242) loading the packs installed in a folder and bundles carrying them (ADR-0243) landed; every host taking a bundle's packs, and the editor taking a pack added to its folder while it runs, landed, and so did socket ranges and choices, named outputs and the Interval module for running with memory; Effects is written as a pack; installing from a file landed; installing from the preset site is next.

## What is wanted

Module plugins in a form that is data, not a .NET assembly, so they install where loading
a DLL is not allowed: iOS (ADR-0211), the web editor, Android (ADR-0184) and the stores.

## The shape

**A pack is text, not raw ops.** A module is a `def` in the patch language, so it compiles
through the same compiler as any patch and runs on the interpreter, IL, GLSL and
JavaScript backends with nothing new. A serialized op list was weighed and set aside: it
ties the format to opcode numbers and register layout, which change between releases
(the `saved-data` skill), and nobody can read or review it.

What the text already expresses:

- **State.** A back-wire is a cell to the ear and a plane to the eye (ADR-0075), which is
  what `UnitRead`/`UnitWrite` and `PlaneRead`/`PlaneWrite` do in C#.
- **Delay lines** through the core `delay` module, **samples and images** through
  `sample` and `picture`, reading files the pack carries.
- **Unrolled work** through `for`, `if`, `sum`, positions and `fold`.

What it cannot, and should not try to:

- **A table worked out in C# as the patch is built**: Speak and Mouth (text into
  phonemes), Path (reading SVG, OBJ and PNG), Text (its font atlas).
- **An editor of its own in the inspector** (the drawing and speech panels). A choice
  setting can be a constant argument.
- **Sound and MIDI backends**, which stay native and linked in.

## What the language needs besides `fold`

Each is marked `NEW` in the Raymarch sketch, `/mnt/project-files/plugin-packs/raymarch.fbks`
in the project's shared files.

1. **A header** (landed, ADR-0241): `pack <id> <semver>` and `language <n>`, read into `LanguageLoad.Pack`.
2. **`module name(...) = { ... }`** (landed, ADR-0241): a def the pack offers by id
   (`<pack id>.<name>`), with `///` lines that become its card, read into `LanguageLoad.Modules`.
3. **Socket ranges and choices** (landed, ADR-0241): `travel = 0 in 0..100`, and
   `steps = 32 in [16, 32, 64]` shown as a setting, one body built for each way they combine.
4. **Named outputs with help** (landed, ADR-0241): `out depth = ... "How far each ray went."`
5. **"Running with memory"** (landed, ADR-0241): the Interval module's `seconds` and
   `remembers`, the values `Emitter.Interval` and `Emitter.HasMemory` give a C# module.

## Placing (landed, ADR-0242)

A pack's module is one module in the catalog, expanded into its body just before the
compiler walks the patch. Still to do: panel knobs and MIDI linked to its sockets, a probe
or meter on the module itself.

## Installing and bundles (landed, ADR-0243)

Each `.fbks` in the packs folder (`packs` in the data folder on a desktop, `Packs` in the
app's own folder on a phone) joins the catalog as `PluginHost.Load` and the device editor
build it. A pack with a mistake adds nothing and is listed with the plugins' problems, by
file and line. A bundle carries each pack its patch uses as `packs/<id>.fbks`, read into
`LoadedBundle.Modules` where the pack is not installed. The editor takes a bundle's packs
into its module list while it runs (`PluginCatalog.Take`). A pack added to the folder, or
written again, joins the list the next time the module list opens, a file opens or a
program driving the editor calls (`PackShelf`). The viewer, the CLI and the web
viewer compile a bundle with its packs too. A pack file opened or dropped on the editor is
installed rather than opened (`PackShelf.Install`): checked, written as `<id>.fbks` over any
earlier version, and its modules join the list. `flyback-cli packs install` and `packs list` do
the same from a script, kept out of the help while packs are dark. Still to do: the web editor,
which has no folder, installing from the preset site, and removing a pack.

## Saving, versioning and checking

- **A `.fbks` or `.fbk` never carries a pack's text** (the user's call, 2026-11-26): the
  module's id names its pack, and a `.fbk` at most notes the pack versions it was saved
  against. **A bundle carries the packs** its patch uses, as `packs/<id>.fbks` beside the
  samples, so a shared patch opens anywhere, offline.
- **A patch whose pack is not installed** opens as one needing a missing plugin does,
  saying which pack it needs and offering it from the preset site where it is there. A newer
  installed version is used, and the editor says so once.
- **A pack has its own semver and names only core modules and sockets**, which saved
  patches already promise to keep working. The plugin contract gains nothing.
- **Building a pack always ends**: loops unroll over written lists, nothing recurses, and
  the 4096-pass limit holds. What it builds runs as any untrusted patch does. File reads
  stay inside the pack (the `outside-input` skill), and the modules a pack may unroll to
  are capped. No "allow this plugin" prompt (ADR-0158) is needed.

## Stores

- **Google Play** allows code run by an interpreter inside the app.
- **Apple 2.5.2** forbids downloaded code that "introduces or changes features", and has
  been enforced harder in 2026. A pack is safest presented as a document the app's own
  engine opens, as Audulus or Pd patches are, never as "plugins that add features".

## How much moves

About 50 of the 61 modules in the module plugins: Effects (4), Voice (12), Mastering (7),
Easy (2), Figures (3), Fractals (3), Picture (all but Text) and Drawings' Rotate, Scale,
Translate and Perspective. Not Speak, Mouth, Path or Text.

## Order

1. The five language pieces above, each landing dark (landed).
2. Port Effects as one pack (landed, dark: `src/packs/flyback.effects.fbks`). `EffectsPackTests`
   holds it to the plugin by type id, sockets, and sound and picture sample for sample. The ops
   are not the same, since the plugin computes outputs nobody wired and a Sine adds its
   `amp` and `bias`. Moving the plugin over still needs saved Echo settings carried from the
   `echo` key to `pack`, and the pack shipped in the app rather than a folder.
3. Install a pack from a file (landed) and from the preset site; load packs on iOS and the web.
4. Move the other plugins' modules over one plugin at a time.
