# The editor's features in an order of their own

Planned on 2026-10-11, on `main` at `61ec9f2`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

[ADR-0194](../adr/0194-the-editors-folders-stand-on-a-footing-that-names-no-feature.md)
put the editor's folders on a footing that names no feature, and left the features free
to name each other. This is the rest: the features in an order too, held by
`FolderLayeringTests` the same way, so a feature reads with only what is below it in
view.

It changes how a few classes work, not only where they are filed, and that is where it
would make the code simpler rather than easier to find: `PresetSlot` takes ten
collaborators, and `Toolbar` and `TakeRecording` each take the other.

## Where it stands

Eighteen pairs of features each have a `using` for the other. The pairs are counted from
`using` lines; what each side names was read by matching type names, not compiled.
Setting the order in `FolderLayeringTests` and reading its failures is the check.

| Pair | Proposed: above | What the lower one names, and the cut |
|---|---|---|
| Assist, Canvas | Assist | `CanvasTips` reads `AssistantPanel.UndescribedNote`. The note moves to `Canvas`, beside the tags it describes. |
| Assist, Hubs | Assist | `PatchFiles` takes `AssistantConversation`, `Playback` takes `ChosenAssistant`. Each reacts to a notice the hub raises. |
| Assist, Inspect | Assist | `Inspector` reads `AssistantPanel.UndescribedNote` too; the same move cuts it. `Assist`'s `using` for `Inspect` names nothing. |
| Bars, Canvas | Bars | `Measuring` takes `Toolbar` to drive its Measure button. The toolbar sets the button from a notice the measuring raises. |
| Bars, Capture | Bars | `TakeRecording` takes `Toolbar` to drive its Record button, while `Toolbar` reads `TakeRecording.RecordTip`. The toolbar sets the button from what the take raises. |
| Bars, Gallery | Bars | `PresetSlot` draws with `ToolbarButtons`, a widget factory that moves to `Controls`. |
| Bars, Hubs | Bars | `EditState` takes `Toolbar` to enable Undo and Redo; the toolbar reacts to `EditStateChanged` instead. |
| Bars, Windows | Windows | `TransportControls` holds the open `PictureWindow`. |
| Canvas, Hubs | Hubs | `Measuring` takes `Playback`, `CanvasSection` names `SourceView`, `Palette` takes `TextWriteBack`. The palette's write goes through a notice the hub reacts to. |
| Canvas, Knobs | Knobs | `Palette` and `ModulePalette` name `PanelKnobs` and `PanelInstrument`. |
| Capture, Files | Files | `TakeRecording` names `PatchFileKinds`, which moves to `Shell` with the other file types. |
| Capture, Hubs | Capture | `Playback` takes `RecordingState`, which moves to `Hubs`. |
| Files, Gallery | Gallery | `FilesSection` takes `PresetSlot` for the startup preset's row. |
| Files, Hubs | Files | `PatchFiles` names `PatchFileKinds` (moved, above), `EditState` takes `UnsavedWork`. |
| Hubs, PluginPackages | PluginPackages | `PatchFiles` takes `PluginInstalls`, `Playback` names `PluginSummary`. |
| Inspect, Midi | Inspect | `Midi`'s `using` for `Inspect` names nothing. |
| Knobs, Midi | Knobs | `MidiSection` takes `PanelKnobs`. |
| PluginPackages, Site | PluginPackages | `SiteAccess` names `PluginSite`, which moves to `Site`. |

Read together, the order comes out near: `Midi`, `Canvas`, `Knobs`, `Hubs`, `Capture`,
`Files`, `Gallery`, `Site`, `PluginPackages`, `Inspect`, `Assist`, `Bars`, `Windows`,
then `Keys` and `Decide`, which pair with no feature.

## How to land it

One commit per pair or two, each green, smallest first: the stale `using` lines and the
moved types, then the constants, then the cuts that become notices. The last commit
replaces "every feature alike" in `FolderLayeringTests` with the order, amends ADR-0194,
and rewrites the guide's folder paragraph.
