# ADR-0194: The editor's folders stand on a footing that names no feature

**Status:** Accepted · 2026-10-11 · *user-directed* · held by `FolderLayeringTests`

## Context

`Flyback.Editor` is a folder per feature, but nothing said which folder may name which.
Thirty pairs of folders each had a `using` for the other, and the root namespace was in a
pair with all twenty folders without a `using` showing it: a child namespace sees its
parent's types for free, and the root held both the composition, which names every
folder, and `ReportLine`, which every folder names. `Settings` held the window's
pages beside what every section is built from, `Shell` built this machine's folders out
of six features' defaults, and `Notices` carried a canvas type.

Read file by file, most of the code was not tangled. Each two-way pair came from a few
types filed in the wrong folder, and the one real knot of mutual references runs
through `PresetSlot`, `Playback` and `Toolbar`.

## Decision

**The features stand on a footing, under the composition.** The footing, lowest
first, is the root, `Controls`, `Updates`, `Statistics`, `Shell`, `Notices` and
`Settings`. Each names only what is below it, and none names a feature. The features
(`Hubs`, `Canvas`, `Inspect` and the rest) name the footing and each other. Nothing
names `Composition`.

**The root is the bottom.** A parent namespace cannot see its children without a
`using`, so once the root names no folder, every way up the order is a line the test
can read. The composition and the hubs leave the root for `Composition` and `Hubs`.

**Whatever lists every feature is composition.** `EditorServices`, `CanvasServices`
and `AddPart`; `EditorView`, `ShellLayout` and `MainWindow`; the start; `ThisMachine`,
which builds this machine's folders and host from each feature's defaults; and the
settings pages, with the sections for what has no folder of its own or whose folder
is in the footing: the picture, the sound, privacy, usage and updates.

**The order is read from the compiled editor.** `FolderLayeringTests` takes each type's
fields, signatures and IL, so a sibling named through its short name, or the root
named through no `using` at all, still counts.

## Consequences

- A new tangle fails the gate with both type names, where before it went unnoticed.
- The features are not ordered among themselves. Ordering them would mean cutting the
  knot through `PresetSlot` and `Playback`, which is a change to how those classes work,
  not where they are filed.
- A small type the footing carries moves down with it: `SocketPick` into `Notices`,
  `InspectorRows` into `Settings`, `PresetMotion` and `Boxed` into `Controls`, and the
  file types into `Shell`.
- `Shell.FileKind` shares a name with the extras' `FileKind`, so the inspector's file
  rows name the extras' one through an alias.
