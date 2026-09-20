# ADR-0121: The window is left as it was left

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

Every launch opened a 1280 by 800 window in the middle of the primary monitor
with the panels where the code puts them, whatever the last session had done to
them. Somebody who works maximized on a second monitor, with the text view open
and the picture swapped in, rebuilt that by hand each time.

Flyback is also run more than once at a time, and the platform then cascades the
windows so each can be seen. A remembered position would put every copy on top of
the last.

## Decision

**The layout is written to `layout.json` in the per-user data folder when the
window closes, and read when it opens** (`WindowLayout`, `MainWindow.Layout.cs`).
Nothing in it is load-bearing ([0034](0034-settings-in-a-file-the-key-in-the-operating-system.md)):
a missing or unreadable file is the default layout, and hand-edited numbers are
brought into range.

It holds the maximized state, the monitor, the client size, the weights of the
two columns and the preview and inspector rows, the assistant's width and the
knob panel's height, whether each of those two panels is open, whether the text
view is showing, and whether the preview and canvas are swapped.

**No position is stored.** The startup location is left to the platform, which
cascades a second copy. Once the window is up, it is moved to the saved monitor
if the platform chose another, keeping its offset from the corner of its monitor
so cascaded copies stay cascaded. A monitor that has gone leaves the window where
it is, and the size is shrunk to fit whichever monitor it ends on.

**The size is the last one the window had while not maximized.** It is taken from
a drag of the frame, or from the client size when the window closes in its normal
state, so maximizing is never read as a size to come back to. Leaving maximized
gives a window of the size it had before. Full screen is not a state that is kept:
the layout written is the one it put away.

A monitor is matched by name and place, then by place, then by name when that is
unique, since a platform may give no name and identical monitors give the same one.

## Consequences

- Every attempt to close writes the file, including one the unsaved-work question
  then cancels. A crash or a kill writes nothing, so the layout is the one from
  the last orderly close.
- Copies closed in turn overwrite each other; the last to close wins.
- The text view opens on the first patch, as if the button had been pressed, so
  a launch that ends in the text view prints the patch for reading.
- A swap is only kept while the first patch has a picture, since that is the
  button's own rule.
- The startup location changes from centered to the platform's. Verified on
  Windows, where it cascades; the other platforms are not checked.
