# ADR-0080: Record moves to the toolbar, with a glyph and Ctrl+R

**Status:** Accepted · 2026-09-16 · *user-directed* · amends
[0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)'s
Output panel, itself [0037](0037-one-output-block-that-every-patch-has.md)'s

## Context

`Record…` has lived under a "Record" heading on the Output's own panel since
[0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md)
left it the only thing there. Reaching it meant selecting the Output first —
a step every other toolbar action (open, save, undo, tidy) does not require,
and the one recording answers least: whether to capture the performance
running right now is a question about the session, not about the module
that happens to own the sockets it reads.

Every other toolbar button had also already dropped its label for a glyph,
open and save's for the reason a folder and a floppy disk are drawn rather
than typed — see `Glyphs`. `Record…`/`Stop` was the one control left
switching a word to say what it meant, sized to the wider of the two labels
so the button did not resize under the take it was running.

## Decision

**`recordButton` is a toolbar button, in its own group between the patch
tools and the program group.** Not inside `patchwork`: recording is not an
edit `Ctrl+Z` takes back, so it does not sit with undo, redo and tidy.

**Its content is a glyph, not a label — `Glyphs.Record()` and
`Glyphs.Stop()`, swapped the same places `"Record…"` and `"Stop"` were.**
Both are filled rather than stroked, unlike every other drawn icon: a record
light is a dot, and a dot outlined at sixteen units reads as a ring, not a
light. `Glyphs` gained a second helper, `Filled`, beside `Stroked` — the two
share `ForegroundBinding()`, pulled out of `Stroked` rather than duplicated,
since both read the same ancestor `ContentPresenter` for the same reason: a
disabled button dims by setting `Foreground` there, not on the `Button`.

**`Ctrl+R` starts and stops a take, wherever the focus is.** Routed through
`MainWindow.OnKeyDown` beside `Ctrl+Z`, `Ctrl+Y` and `Ctrl+L`, guarded the
way a click on a disabled button already is: the new `ToggleRecordAsync` —
shared by the click handler and the shortcut — returns at once if
`recordButton.IsEnabled` is false, so a patch with nothing to record ignores
the key the same way it already greys out the button.

**The Output panel keeps Rewind and nothing else under "Sound."** The
"Record" heading and the button under it are gone from `BuildOutputSettings`;
`WireOutputControls` still wires `recordButton`'s click and its
`ToolTip.SetShowOnDisabled`, since those are the state of the instrument
regardless of which control shows it.

**The tip changes while a take is running, not just the glyph.** A glyph
alone answers "what does this button do right now", and losing the label
loses the one place `"Stop"` used to say it. `MarkRecordable` — already the
one place that decides `IsEnabled`, and already re-run on every recompile,
since ADR-0021 recompiles on every edit — now decides the tip from the same
three-way branch: `StopTip` while `recorder` is set, `RecordTip` when the
patch reaches something, `NothingToRecord` when it does not. `Start` calls
it once more after setting `recorder`, since starting a take is not itself
an edit and would otherwise leave the old tip showing until the next knob
turn.

**`RecordTip` no longer says "an export."** ADR-0078 already removed the
Output panel's `Export…` button, so a tip written to distinguish Record from
a button beside it that no longer exists could only read as describing UI
that is not there. It now names `flyback-cli render` directly.

## Consequences

**Recording is reachable with nothing selected**, which is the point: a
session that never opens the inspector can still capture a take.

**A glyph carries no text, so `OutputSettingsTests`'s `Record` helper finds
the button by name now, not by content** — the same trade every other
toolbar button already made, and `ShowingSettings`'s tell for the panel's
presence moves from `Record…` to `Rewind`, the one standalone control left
there.

**The tip is the only place the button still says what it does**, same as
every glyph beside it — `RecordTip`, `StopTip` and `NothingToRecord` between
them cover every state the button can be in, and `RecordTip`/`StopTip` each
carry their own `(Ctrl+R)` the way `undo`'s and `tidy`'s tips already carry
theirs.
