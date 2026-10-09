# ADR-0148: The window is its hubs and the regions around them

**Status:** Accepted · 2026-09-24 · *user-directed* · supersedes the decision of
[0039](0039-one-window-class-across-a-file-per-region.md); keeps
[0016](0016-build-the-ui-in-c-sharp-without-xaml.md)

## Context

0039 split `MainWindow` into partial files and declined collaborator classes,
because every region seemed to need four or five of the window's fields passed
in and handed back. The window has since reached about 9,000 lines across
twenty files, and one scope over all of them.

Counting which region calls into which shows where that coupling actually runs.
Of the calls one region makes into another, most land on two files:
`MainWindow.Engine.cs` (88, of which 77 are `Report`, a one-line wrapper around
the `ReportLine` control, and the rest recompile, pause and reopen the sound) and
`MainWindow.Source.cs` (60: a knob turned, a hand come off, undo, redo, the text
taken or dropped). Of the window's 154 fields, six are read across regions: the
canvas, the preview, the sound, the plugins, the assistant and the output
settings. Everything else belongs to one region, or at most two.

So the regions are not coupled to each other. They are coupled to a report line
that is already a class, and to hubs that exist only as methods on the window,
so a region extracted alone has to reach back into the window for them. That is
the cost 0039 measured.

## Decision

The hubs become classes that own no controls of the window's:

- **`Document`** owns which of the canvas and the text is the document
  (0068), the write-back from the panel into the text, and which stack an undo
  lands on (0071). Regions tell it what happened (`Turned`, `Edited`,
  `HandCameOff`) and it decides what is written.
- **`Playback`** owns compiling, the sound device, and pausing, muting and
  rewinding the pair.
- **`PatchFiles`** owns which file the patch is: its name, whether it is a
  bundle, what the bundle carries, where the files it names are read from, and
  opening and saving. Presets, recovery and takes read it for the name and the
  folders.

A region that has something to say takes the `ReportLine` itself.

Each region then becomes a class that takes the hubs and whatever else it reads,
owns its own fields, and says what happened rather than calling back into the
window. `MainWindow` is what lays the regions out and asks the closing question.

**A hub says what happened as a notice, and whoever cares reacts to it.** A
notice is a record in `Flyback.Editor.Notices`, one file each, carrying what a
reactor needs: `PatchChanged(Opened)`, `PatchCompiled`, `DocumentArrived(Sounds,
Pictures)`, `TakeMarked`. A part that reacts declares it in its class header,
`Inspector : IReactTo<SelectionChanged>`, and `Reactions` hands each notice to
every reactor the container holds, lowest `Priority` first, ties in registration
order. Reactors are looked up when the notice is raised, never while the
container is built, so a reaction is never a constructor dependency: the raiser
does not take the reactor, the reactor does not take the raiser, and no reaction
can close a cycle however the logic later moves. A new dependency that would
close one becomes a notice instead.

A raiser that cannot wait calls `Raise`, which runs what finishes at once and
throws a later fault on the UI thread, as an unhandled event handler would; one
that can wait calls `RaiseAsync` and gets every reaction's task in turn. A
reaction is a `Task`, so a reactor that asks a question (`UnsavedWork` to
`RestartAsked`, `HandBackAsked`) awaits it, and a spec awaits the whole chain.

The toolbar's buttons and the window's keys raise the same notices, `SaveAsked`
from the Save button and from Ctrl+S, so what a command does lives in one
reactor. `Toolbar` takes nothing it acts on.

What stays a C# event: a control telling the region that owns it
(`ControlsPanel` to `PanelKnobs`, `SourceView` to `Document`), and what the
viewer shares with the editor and so cannot raise an editor's notice
(`MidiHub`, `PreviewHost`, `IlCompiler`), which the part that already takes it
subscribes to in its constructor.

Three things are refused, each because it would only ever show up as a test
that fails one run in fifty. Every reactor is built with the window, so no
notice raised later constructs one mid-chain, and a notice raised while the
window is being built throws: what a part has to say at start it says from
`Start`. A notice raised off the UI thread throws at the raiser rather than
reaching a control from the wrong thread. A notice raised after the window's
container is disposed goes nowhere, and a chain still awaiting stops at the
next reactor, so nothing touches a closed window's controls.

The hubs, the regions, the notices and the window are composed in a container
([0150](0150-the-editor-is-composed-in-a-container.md)); `AddPart<T>` registers
a part and every reaction its class declares.

## Consequences

A field is private to the region that owns it, and the compiler enforces the
split that 0039 could only keep by convention.

A feature is one file and one registration line: a reactor declares what it
reacts to, and nothing else is edited for it to be heard. A search for a
notice's name finds its raiser and every reactor by class name. The order
reactors run in is a number on the reactor, not the order lambdas happen to
sit in.

`MainWindow` owns the window's layout, keys, closing question and full screen,
and stays one class in one file. `ShellLayout` owns the editor grid and its
panels. There is still no binding layer and no view model: state lives in the
`Patch`, and 0016 stands as written.

## Amendment, 2026-10-09: the write-back and the caret are parts of their own

`Document` keeps who owns the patch, the undo landing, applying and which view
shows. The write-back is `TextWriteBack`: regions tell it what happened
(`Turned`, `Edited`, `HandCameOff`, `PanelEdited`) and it writes into the text
through the map and the undo landing `Document` keeps. `CaretFollow` points the
inspector at what the caret stands on and says whether the text has moved on
from it (`IsAdrift`), and `TextWriteBack` holds it still while it writes.
`PastedPatch` writes a pasted patch file as text. `Document` takes
`CaretFollow` and `TextWriteBack` takes `Document`, so nothing closes a cycle;
a `TextForgotten` notice tells the write-back to drop what it was waiting to
write when the text comes to mean something else.
