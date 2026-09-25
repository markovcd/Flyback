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

Each region then becomes a class that takes the hubs and whatever else it reads, owns its own fields, and raises events rather than
calling back into the window. `MainWindow` is what builds the hubs and the
regions, lays them out, and asks the closing question.

A hub says what changed through events, and whoever cares subscribes. It never
names a region.

The hubs, the regions and the window are composed in a container
([0150](0150-the-editor-is-composed-in-a-container.md)). Two pairs need each other
(the take and the playback, the files and the playback), and a `Lazy` settles each.

## Consequences

A field is private to the region that owns it, and the compiler enforces the
split that 0039 could only keep by convention.

The regions move one at a time, each in a commit that builds and passes on its
own. What stays is what the window is — its layout, what each toolbar button
does, its keys, the closing question and full screen — and it stays as one class
in one file, a `#region` per part, rather than as partial files: once the regions
are classes, what is left is small enough to read top to bottom.

There is still no binding layer and no view model: state lives in the `Patch`,
wiring is event handlers, and 0016 stands as written.
