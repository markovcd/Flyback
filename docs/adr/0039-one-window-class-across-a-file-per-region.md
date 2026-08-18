# ADR-0039: One window class, across a file per region

**Status:** Accepted · 2026-08-18

## Context

[0016](0016-build-the-ui-in-c-sharp-without-xaml.md) declined XAML and put the
whole shell in `MainWindow.BuildLayout()` and its friends. It closes by saying
the structure is "kept to one `Build*` method per region", which was true and
was enough while the file was six hundred lines.

It reached seventeen hundred. The regions are still there and are still the
right seams — the file has carried `// --- inspector ---` style banners for most
of its life — but a banner is not a boundary. Finding the export code meant
scrolling past the palette; two unrelated pieces of work touched the same file
every time; and the one thing the banners could not say, which is *what a region
is for*, had nowhere to be written down.

Three ways out were available.

**Extract collaborator classes.** An `Inspector`, a `PatchFiles`, a
`Palette` — each a real type with its own state. This is the answer for a shell
with independent regions. It is not what this one has: the inspector reads the
selection off the canvas and writes the Output's settings straight into the
preview and the audio engine; the export button's enabled state is decided by
the compiler's result; the palette's filter is redrawn when a plugin list
changes. Every candidate class would need four or five of the window's fields
passed in, and would hand most of them straight back through callbacks. The
coupling is real rather than accidental — this is one instrument, not four
panels — so the extraction would move code without separating anything.

**A view model layer.** Rejected upstream, by 0016. There is no binding layer
here on purpose, and adding one to make a file shorter would be the tail wagging
the dog.

**Partial classes, one file per region.** The class stays exactly what it was;
what changes is which file each part of it is written in.

## Decision

`MainWindow` becomes `partial` across six files, named for what each owns:

| File | |
|---|---|
| `MainWindow.cs` | the controls the window is made of, the constructor, and the layout that arranges them — including the toolbar and the status bar |
| `MainWindow.Palette.cs` | the module list, its filter, and the per-plugin ticks |
| `MainWindow.Inspector.cs` | the panel on the right: a module's knobs, and the Output's own settings |
| `MainWindow.Files.cs` | opening, saving, still frames and both kinds of export |
| `MainWindow.Engine.cs` | opening the sound device, recompiling on every edit, and the status bar's contents |
| `MainWindow.Editing.cs` | undo and redo, and the question every route out of a patch has to ask first |

Each file carries a class-level doc comment saying what that region is and what
is peculiar about it. That comment is the thing a banner could not be.

The rule for what goes where is that the window's **controls stay in
`MainWindow.cs`** — every field that is a button, a panel or a text block is
declared in one place, so the shape of the window can be read without opening six
files — while **methods, nested types and single-region constants move to the
file that uses them**. A flag that only one region reads, like the one that stops
the closing question being asked twice, goes with it.

## Consequences

The largest file drops from about seventeen hundred lines to under five hundred,
and none of the six exceeds it. Nothing about the program changed: no member was
added, removed, renamed or made more visible, and the compiled output is the same
class it was.

The cost is that C# gives no way to say a field is private *to one file*. A
partial class is one scope, so `MainWindow.Palette.cs` can reach the export
button as easily as `MainWindow.Files.cs` can. The split is a convention the
compiler will not enforce, and it will hold exactly as long as it is kept
deliberately. Real classes would have enforced it, at the price described above.

The second cost is that the constructor now calls into four files, so the order
things are set up in is less obvious than when it was all one scroll. It is
still one constructor, and it still reads top to bottom.

Field initialisers are worth one note. They run in declaration order, and across
partial files that order is whatever the compiler happens to pick — so an
instance field whose initialiser reads another instance field would be a hazard
here that it is not in a single file. None does; every initialiser is a literal
or a `new`, and the two that read anything read static members, which are
initialised separately.

This is a decision about a file, not about the design, and it deliberately
changes nothing that 0016 or [0017](0017-draw-the-node-editor-in-one-control.md)
settled. If a region ever does become independent — an inspector that reads a
selection and raises events rather than reaching into the preview — it should
become a class, and this record is not a reason to leave it as a partial file.
