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

**The features stand on a footing, in an order, under the composition.** The
footing, lowest first, is the root, `Controls`, `Updates`, `Statistics`, `Shell`,
`Notices` and `Settings`. Each names only what is below it, and none names a feature.
The features stand on it, lowest first: `Site`, `Decide`, `Midi`, `Canvas`, `Hubs`,
`Assist`, `Knobs`, `PluginPackages`, `Capture`, `Files`, `Gallery`, `Inspect`, `Bars`,
`Keys` and `Windows`. Each names only the footing and the features below it. Nothing
names `Composition`.

**What a feature needs from above it arrives, rather than being fetched.** A notice
(`TakeMarked`, `MeasureModeChanged`, `PatchRelaid`, `SoundFailed`, `PluginsMissing`,
`StartupPatchAsked`), which carries an answer back where one is needed, as
`RestartAsked` already did; or an interface the lower feature defines and the upper
one fulfills (`IPatchConversation`), as `Shell` does with `ITitle`. A settings section
sets what is below it (the MIDI section is the knob panel's, and sets the panel's
grid); what is above it reads the section (the text view reads its font size from the
canvas section).

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
- Ordering the features cut the knot through `PresetSlot`, `Playback` and `Toolbar`.
  The toolbar draws its own Undo, Redo, Measure and Record from the document and the
  notices, where it was driven from three features; `PatchFiles` and `Playback` raise
  what the assistant and the plugins react to; and `PresetSlot` no longer draws with
  the bar's factory or is taken by the Files section.
- A small type the footing carries moves down with it: `SocketPick` into `Notices`,
  `InspectorRows` into `Settings`, `PresetMotion`, `Boxed` and `ToolbarButtons` into
  `Controls`, and the file types and `PatchFileKinds` into `Shell`. `RecordingState` is
  a hub, `PanelInstrument` an instrument, and the window title is kept by `PatchTitle`
  in `Files`.
- `Shell.FileKind` shares a name with the extras' `FileKind`, so the inspector's file
  rows name the extras' one through an alias.
