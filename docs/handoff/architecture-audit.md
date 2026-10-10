# Where the architecture is weakest, and the refactors that would fix it

Written on 2026-10-08, on `main` at `a00af7c`. An audit, not a plan: each item is
a proposal, and goes to TODO.md only once the user picks it. Delete an item here
in the commit that lands it, and the file when the last goes.

Code lines are `grep -cvE '^\s*(//|$)'`; comments run a third to two thirds of
most files here. Everything was confirmed by reading; nothing was run.

What holds and is not raised again: the layers reference one way; Engine takes no
packages; Core references nothing; the container is used uniformly across the
three editor hosts, with no hand-built service outside composition; every public
*type* in Core and Plugins is named by a plugin; the backends are transcriptions
by decision (ADR-0035); the binder is one walk (ADR-0183); presets are C# by
decision (ADR-0138).

## 10. Tests (Medium to Low)

- **Four files hold a fifth of Editor.Tests.** `AssistantPanelTests.cs` 1,186 code lines, `SourceViewTests.cs` 1,154, `OutputSettingsTests.cs` 943, `NodeEditorTests.cs` 676; Split by rule group.

## Order

Each as its file is next touched.

Landed: the host code that is not Avalonia has a home, `Flyback.Host`
(ADR-0188), and with it one plugin bootstrap, one bytes-to-patch reader, one
settings reader for the CLI, one playback registration and one preset finder.
The infix bracket rule is `Infix` in Core, which the binder, the printer and the
fusing all spell a sum through; the three `Strength` tables' negative-number
arms turned out never to change a spelling, and went with them. `OpShape` names
every opcode, refuses one it does not, and owns the "registers an op reads" loop
and the line-or-cell question for every pass and backend. The preset gallery's
parts are top-level types rather than partials of `PresetGallery`, and the canvas's
gestures are `ModuleDrag`, `WireDrag` and `RubberBand` under `CanvasGestures`.
`Document` keeps ownership, the undo landing and applying; the write-back
is `TextWriteBack`, the caret's selection `CaretFollow`, and a pasted patch
file `PastedPatch`.
`ContractSurfaceTests` checks members as well as types, and what only the host
called is internal. The preset site's admin client is `Flyback.Site.Client`, which
`flyback-site` and `render-presets` both send through, and the site references
every plugin a page may link, Drawings included. `CompiledPatch` describes the
program; `Interpreter` runs it and `Arithmetic` holds the guards it and the IL
share. A tool's arguments are `ToolFields`, which its schema is rendered from and
its body reads through. A part with something to do at start
declares an `IStartAt` phase, and `Startup.Load` returns a `DesktopLaunch`.
Still written per shell, and small: the speaker handshake in two JS files, the
viewer's and the CLI's `--size` and `--oversample` option declarations, and the
viewer's `--cpu` against the CLI's `--processor`.
