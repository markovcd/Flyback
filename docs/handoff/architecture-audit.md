# Where the architecture has drifted, and what would fix each point

Written on 2026-10-11, on `main` at `b9ad289`, the second pass after the one that
landed on 2026-10-10 (`7328cc8`, `767dc4a`, `9872074`). An audit, not a plan: each
open item is a proposal, and goes to TODO.md only once the user picks it. Delete an
item here in the commit that lands it, and the file when the last goes.

What was measured: the project references of every `.csproj`; code lines per project
and per file (blank and comment lines left out); which project names each type in
`Flyback.Ui` and `Flyback.Host`; the `using` lines between the editor's folders; each
notice's raisers and reactors; the longest stretch between two member declarations in
every file; and every backticked identifier in the guide, the glossary, the threat model
and the C4 model against the tree. Item 1 was confirmed by a test that failed; the rest
by reading.

What holds and is not raised again: the references run one way and down, with no cycle
(`ComponentDiagramTests` holds the editor's to the C3 view); `Flyback.Core` references
nothing, `Flyback.Engine` only Core (ADR-0019), `Flyback.Site.Client` nothing at all;
`Flyback.Plugins` names no Engine type in its public surface, so its private reference
to Engine leaks nothing; the in-box plugins see one internal of Plugins,
`CategoryAccents`, for the reason written beside the grant; `internal` everywhere with
`InternalsVisibleTo` for the host's assemblies is the stated style, not a leak; every
type in `Flyback.Host` the editor alone names is a part of `OutputSettings`, which the
CLI and the viewer read; `IPresetFolder`'s one implementation is the seam Host reads
the editor's folder through; `ShellLayout`'s twenty-one constructor parameters are the
layout taking every panel, which ADR-0150 asks for over a bundle; the static state that
remains is the web viewer's exports (a `JSExport` is static), `MidiSources`'s hook
(its header says why) and two caches; `Graph` and `Language` in Engine refer to each
other only through `PatchFile`, the one door (ADR-0065); and nothing the threat model,
the C4 model or the guide's ADR links name is missing from the tree.

| Project | Code lines | Files |
|---|---|---|
| `Flyback.Editor` | 28,854 | 340 |
| `Flyback.Engine` | 15,219 | 185 |
| `Flyback.Core` | 6,861 | 121 |
| `Flyback.Plugins` | 6,258 | 133 |
| `Flyback.Cli` | 5,172 | 58 |
| `Flyback.Ui` | 3,623 | 53 |

## 1. The canvas's notice reached nobody (Medium, done in `edd94fc`)

`CanvasReport.Say` raised a `CanvasSaid` for everything the canvas has to say of an
edit ("Ungrouped.", "A group needs 2 modules", "Clipboard unavailable"), and its
header said the report line reacts. No class in `src/` declared
`IReactTo<CanvasSaid>`: only the tests subscribed, with `Reactions.Add`, so every
canvas test passed while the shipped editor said nothing. A window-level test
(`CanvasReportTests`) opened the editor, declined a group and read the report line's
history: empty.

**Fix:** `StatusBar` reacts to `CanvasSaid` and says it on the report line, and the
test stays. The lesson for the rule: a test that subscribes to a notice itself proves
the raiser, not the editor; a notice meant for the window is proved on the window.

## 2. `Flyback.Ui` held five types only the editor names (Low, done in `c0b5367`)

The pass before moved `InstrumentLibrary` out of Ui and left its profile types
(`InstrumentProfile`, `InstrumentPage`, `InstrumentTrack`, `InstrumentControl`)
behind, and `Markdown` landed in Ui the same day for the assistant's column, which the
viewer has no equivalent of. Counted by naming: a shell other than the editor, or a Ui
type one reaches, names 46 of Ui's 53 types; the five above are named by the editor
alone, and `GraphicsDrivers` and `TransportServices` are reached by extension method.

**Fix:** the five move to the editor's folders for their features, and `SharedUiTests`
in `Flyback.Ui.Tests` holds the rule from here, with the two extension classes listed
with their reason as `ContractSurfaceTests` lists what is promised.

## 3. Two settings sections sat apart from their feature (Low, done in `3d9a934`)

The guide puts a feature's settings section in its folder, and four are (`Assist`,
`Canvas`, `Decide`, `Files`), with `PrivacySection` composed of the pieces `Updates`
and `Statistics` keep. `MidiSection` sat in
`Settings/` while `Midi/` held the instrument library it configures, and
`RecordingSection` sat there while `Capture/` held the take it configures. Each read
five or six other folders to do it, which is most of why `Settings` was in seven of the
editor's twenty-one two-way folder pairs.

**Fix:** `MidiSection` to `Midi/`, `RecordingSection` to `Capture/`. `PictureSection`,
`SoundSection` and `PrivacySection` stay: the picture, the sound and privacy have no
folder of their own, and the guide now says so rather than implying one.

## 4. The editor's root is a feature folder in disguise (Low)

The guide said the root holds the composition, the window, the hubs and `ReportLine`.
It holds 37 files. Thirteen are the contract a window or a page fulfils for the editor:
`EditorSetup`, `EditorHost`, `EditorFolders`, `EditorLaunch`, `IClose`, `IFocus`,
`ITitle`, `IMonitors`, `IFilePickers`, `IViewer`, `IBrowserStore` and the `No*`
defaults of the last two. Six more are window behavior with no window type in them:
`Attention`, `FullScreenPreview`, `MonitorPlacement`, `Restart`, `Reopen`,
`LastPress`. The rest are the hubs, the composition and the view. The guide now lists
the contract as the root's, which is true and is the smaller change.

**Fix:** a `Shell/` folder (namespace `Flyback.Editor.Shell`) for the thirteen, named
for what the glossary calls the programs around the editor. The desktop, web and
Android programs each implement it, so each gains one `using`. The six behaviors are a
second step if the first reads well; `Windows/` is the desktop's and is not their home.

## 5. The editor's folders refer to each other both ways (Low)

Twenty-one pairs of feature folders each `using` the other, counted before item 3
moved two sections; `Canvas` is in seven, `Settings` was in seven. The repo's rule
against cycles is the container's (ADR-0150: three `Lazy<T>`, and notices so a reaction
is never a dependency), and it holds; the folders have no such rule, and the guide
promises none. Some pairs are the design: `Notices` carries canvas types because a
notice carries what a reactor needs, and a feature's section reads `Settings`' rows.
Others are a part reaching across: `UsageCounter` takes `NodeEditor` and `PresetSlot`
to count what a patch holds when playback starts, where the `PatchCompiled` notice it
could react to already carries the patch.

**Fix:** none until the user says whether folders should layer. If they should, the
rule is one sentence in the guide and a test like `ComponentDiagramTests` over the
`using` lines, and `UsageCounter` is the first move. If not, item 3 was tidiness and
this item closes.

## 6. `Binder.Call` is one method of 175 lines (Low)

`Binder` is 931 code lines in some 47 members, the largest file in `src/`, and ADR-0183
decided two days ago that the walk stays in one class. Inside it, `Call` (lines 857
to 1032) binds a module call in five phases with a comment heading each: the named
arguments, where the pipe lands, the free arguments in order, placing the module,
and what it carries. `Arguments` is 118 lines, `Expand` 58. The ADR is about classes
and says nothing against a private method per phase.

By the same count, the longest stretches elsewhere are `GlslEmitter.Emit` (362
lines), `ExpressionFusion.Fuse` (311), `ViewerArguments.Build` (303),
`RenderCommand.Build` (245), `GroupInspector.Build` (238), `CanvasPainter.Render`
(218) and the `GalleryLayout` constructor (218); the presets' `Build` methods are
data and are left out. The count is lines between declarations, so a method followed
by fields or a nested type counts long; only `Call` was read.

**Fix:** split `Call` by its five headings into private methods of `Binder`, which
keeps ADR-0183 as written. Open each of the others once before deciding; the two
command builders are option declarations and may be as long as they are.

## 7. The docs named what the code no longer calls things (Low, done with this audit)

The guide named `SettingsDialog` (now `SettingsSession`) and listed the editor's
folders without `Decide`, `Keys`, `Midi`, `Notices` and `Windows`; it said a
namespace is its assembly name with no exceptions, and the plugins' shared source
(`Flyback.Plugins.Programs` inside `Flyback.Plugins.Codex`) is one, by ADR-0173. The
glossary named `WebPlayer` and `LetterStore`, neither of which exists. ADR-0140 still
placed the instrument profiles in `Flyback.Ui` after the pass before moved them.

**Fix:** each line says what the code is, and ADR-0140 carries a dated amendment.
