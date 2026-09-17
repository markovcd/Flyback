# ADR-0087: The assistant moves to a column, beside the patch

**Status:** Accepted · 2026-09-17 · *user-directed*

## Context

The assistant has sat in a row under the patch since it was built: `canvas`
was rows — editor or text, the controls panel, then the assistant — all
three stacked in the one column the patch and its tools already owned,
rather than spanning the whole window, because what the assistant is
talking about is the patch (ADR-0069's `AssistantPanel` knows nothing else).
Opening it took a third of that column's height, leaving the conversation
exactly as wide as the patch and exactly as tall as whatever height a
`GridSplitter` last left it.

That shape reads a chat-style panel backwards. A transcript wants to read
top to bottom at a steady width; a node graph wants room to spread left to
right. Row-stacking gave the assistant the patch's full width for a column
of short lines, and gave the patch a slice of height pinched by whatever
the conversation was using. A wider window made the problem worse, not
better — the row split stayed a fraction of the same cramped height while
the extra width went to text that did not need it.

## Decision

**The assistant is the left column of `canvas`, the patch and its controls
the column to its right, `assistantSplitter` a vertical `GridSplitter`
between them.** `canvas` gained `ColumnDefinitions` where it had
`RowDefinitions`; a new inner grid, `patch`, took over the row stack —
editor or text, then the controls panel — that used to be `canvas`'s own.
`assistant` still hangs off `canvas` rather than off `columns` or
`rightPanel` directly, the same as before: `ShowFullScreenPreview`'s
collapse already reads which column to keep off the layout rather than off
a hardcoded index, so nothing there had to change, and the assistant's own
remembered visibility stays outside the "plain yes" reset that column gets.

**The assistant's column is a pixel width, not a star.** The patch's column
is the one left star-sized. A `GridSplitter` between a pixel track and a
star one resizes the pixel track directly and leaves the star one to absorb
whatever the window does — so dragging the splitter changes the
conversation's width the way it always could, but resizing the window
changes only the patch's. The two flexible rows this replaces were both
star-sized for the opposite reason: a window resize was supposed to reach
both, since there was only ever one direction to grow in.

**`assistantShare` is now a pixel `GridLength` — 320 by default — not the
one star it was.** `ShowAssistant` reads and writes it exactly as before,
against `assistantColumn.Width` in place of `assistantRow.Height`, and
`MinWidth` in place of `MinHeight`, kept at 280 while shown for the same
reason 140 guarded the row: a minimum outranks a width of zero, and a
column zeroed while it still had one would hold that much of the shell open
over nothing.

## Consequences

**A wide window now grows the patch, not the conversation.** The assistant
keeps the width it was left at — by the splitter, or by 320 the first time
— across any resize; only the graph gets the extra room.

**`OutputSettingsTests`'s `The_assistant_shares_the_canvas_column` became
`The_assistant_shares_the_canvas_row`.** The invariant it checks flipped
with the axis: the assistant and the editor are no longer visual siblings —
the editor sits inside `patch`, the assistant directly inside `canvas` — so
the test compares their positions through `TranslatePoint` into the
window's own coordinates rather than through a shared parent, and checks
that they share a height (the row) rather than a width (the old column),
plus that a window resize leaves the assistant's width alone.
