# ADR-0124: What two shells draw with is a project of its own

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

The viewer ([0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md)) needs
the editor's preview, its sound device, its look and its settings. Copying them into
a second shell is the duplication the viewer is arranged to avoid, and referencing
the editor from it would bring the whole editor along.

## Decision

**`Flyback.Ui` holds what both windows use:** the preview stack (CPU and GPU), the
audio engine, `Colors`, `Text`, `Glyphs` and the module skins, `OutputSettings`,
`PresetLibrary`, the frame and audio sink interfaces, and `Terminal`. Avalonia
itself and nothing else; AvaloniaEdit and Svg belong to the editor and stay there.

**The namespaces are the project's own:** `Flyback.Ui`, `Flyback.Ui.Controls` and the rest.

**Internals are shared with `InternalsVisibleTo`** for the editor, the viewer and the
tests, rather than promoting everything to public. What was public stays public.

Three things came out of `MainWindow` to be shared: the device opening and the
volume check (`Sound`), the resolution table (`Resolutions`), and the `Takeover`
enum.

## Consequences

- A change to the preview or the look is made once and reaches both windows.
- A type meant for one shell has a place to go wrong: something moved here that
  only the editor needs is carried by the viewer as well.

## Amendment, 2026-10-08

`OutputSettings`, `Resolutions`, `PresetLibrary` and the `Takeover` enum moved on
to `Flyback.Host`, which holds what every host reads and carries no Avalonia
([0188](0188-what-every-host-reads-is-a-project-of-its-own.md)). Ui keeps what
the two windows draw and sound with.

## Amendment, 2026-10-10

`InstrumentLibrary` and its shipped profiles, `PlayheadLine` and `PopupHoles` moved
to the editor, which alone names them: the viewer plays an instrument but keeps no
library of them, has no seek bar and draws no popups over a page's canvas. What
remains in Ui is named by both windows or by Ui itself (`GraphicsDrivers` and
`KeyCodes` reach the shells as extension methods).
