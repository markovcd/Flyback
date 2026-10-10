# What a finger still cannot do

Audited on 2026-09-30, on `main` at `b5003a2e`, by reading the code; nothing here has
been run on a device yet, and the user means to check each repro on a phone before
it is fixed. **Confirmed** means the code path was followed end to end; **suspected**
means the outcome rests on browser, Avalonia or AvaloniaEdit behavior not verified
here. Line numbers are as of that commit. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last item.

A touch screen is a Windows tablet as much as a phone in the web editor, and most of
these hold for both; the two marked **page only** are the browser's.

Every item but 6 is fixed and gone from here; it keeps the number it was first listed
under.

## Out of reach

### 6. A MIDI In preset can't be played (confirmed)

The computer keyboard is a MIDI In's only instrument on the page
(`EditorView.cs:207`). Wants an on-screen keyboard, not a fix.

## Elsewhere

### Minor

Every toolbar and inspector button is a glyph labeled only by its tooltip
(`ToolbarButtons.cs:28-31`); so are the socket help (`Inspector.cs:509`), the
disabled Keep-group reason (640-653) and the "Double-click to rename" hints
(803-806, 917; `ControlsPanel.cs:278`). A finger never hovers, and holding long
enough for the tip clicks the button on the way up.
