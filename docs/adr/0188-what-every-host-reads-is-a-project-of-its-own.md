# ADR-0188: What every host reads is a project of its own

**Status:** Accepted · 2026-10-08 · *user-directed*

## Context

`Flyback.Ui` holds what the two windows draw with, and carries Avalonia
([0124](0124-what-two-shells-draw-with-is-a-project-of-its-own.md)). The CLI, the
preset site and the web viewer cannot reference it, and the engine cannot hold
anything that needs a plugin. So what a shell needs that is neither drawing nor
engine had nowhere to live, and each shell wrote it again: the output settings
were read by hand in the CLI, a preset was found by name in four places, a patch
was opened from bytes in four, the plugins were loaded and installed in seven,
and the viewer built its playback stack a second time and left the microphone
out. The bugs on TODO.md fell into that gap.

## Decision

**`Flyback.Host` holds what every host reads before it has a window, or without
one:** `OutputSettings` and the enums it is made of, `Resolutions`,
`PresetLibrary` and `SavedPreset`. It references Core, Engine and Plugins, and
carries no Avalonia and no packages, so the CLI, the site and the pages reference
it as the windows do. It sits between `Flyback.Assist` and `Flyback.Ui`, and Ui
references it.

**What draws stays in Ui.** A type moves to Host when a program with no window
reads it; the preview, the knobs, the sound device and the transport are the
windows' and stay where they are.

**The namespace is `Flyback.Host`**, flat: the project is small enough that a
folder would be a sub-namespace for one file.

## Consequences

- A setting, a size or a preset is read one way by every program, and a
  shell's command line can offer what the editor's settings offer.
- `Resolutions` lost Avalonia's `PixelSize`; a size is `(Width, Height)`, as
  `FrameSize` already spells it, and a window makes its `PixelSize` at the edge.
- Nothing a plugin is compiled against changed: Host is not in the contract.
