# ADR-0171: Every setting is kept in one file

**Status:** Accepted · 2026-10-02 · *user-directed* · supersedes the separate files
of [0034](0034-settings-in-a-file-the-key-in-the-operating-system.md),
[0082](0082-the-output-settings-move-to-the-settings-window.md),
[0088](0088-a-release-installs-itself-at-the-next-start.md),
[0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md),
[0127](0127-the-person-chooses-what-opens-a-flyback-file.md) and
[0149](0149-compact-modules-share-rows-and-tip-their-values.md); implemented in
`src/Flyback.Core/SettingsFile.cs`

## Context

The settings were six files: `output.json`, `assistant.json`, `canvas.json`,
`update.json`, `usage.json` and `file-types.json`, one per feature as each arrived.
No decision chose that. It cost the settings window a second gathering step: five
tabs kept rows in `output.json`, which had to be read whole from all five before it
could be applied, so `OutputSettingsUse` aggregated the tabs a second time beside
`SettingsSession`.

## Decision

**One `settings.json`, a section a concern:** `output`, `assistant`, `canvas`,
`updates`, `usage` and `fileTypes`. Each settings type keeps its shape and its
place, so the viewer and `flyback-cli` still read only what they need, and reads and
writes its own section through `SettingsFile`. A write rewrites the file with the
other sections as they were, beside it first and then moved over.

**Each settings tab saves and applies its own rows.** The tabs kept in the output
section change the shared `OutputSettings` in place, write the section, and put
what they changed in force: Picture the preview, Sound the device, MIDI the knobs,
Recording ffmpeg, Files the library. `OutputSettingsUse` and the slices it gathered
are gone, and the editor starts by asking every tab to put its saved rows in force.

**The old files are not read.** Before 1.0 there is no saved work to keep
(`saved-data.md`), so the first run with this change starts from the defaults and
leaves the six old files where they are.

The window layout, the plugins somebody allowed and the key stay where they were:
the first is the window's state rather than a setting, the second a record of
consent, and the key never goes in a file (0034).

## Consequences

**A damaged file loses one section at most when only a section is damaged**, and
everything when the file as a whole does not parse; either way the program starts
on the defaults for what it cannot read, and the next save writes a whole file.

**A save of one tab writes the whole file,** so the five tabs in the output section
each write it once when the window is saved. It is a few kilobytes on a click.
