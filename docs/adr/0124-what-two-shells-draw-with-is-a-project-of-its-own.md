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

**The namespaces did not change.** They are still `Flyback.App`, `Flyback.App.Controls`
and the rest, the way `Flyback.Engine` holds `Flyback.Core` ([0002](0002-split-engine-from-shell.md)),
so no file that used them was edited to keep working.

**Internals are shared with `InternalsVisibleTo`** for the editor, the viewer and the
tests, rather than promoting everything to public. What was public stays public.

Three things came out of `MainWindow` to be shared: the device opening and the
volume check (`Sound`), the resolution table (`Resolutions`), and the `Takeover`
enum.

## Consequences

- A change to the preview or the look is made once and reaches both windows.
- A type meant for one shell has a place to go wrong: something moved here that
  only the editor needs is carried by the viewer as well.
