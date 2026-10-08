# What a finger still cannot do

Audited on 2026-09-30, on `main` at `b5003a2e`, by reading the code; nothing here has
been run on a device yet, and the user means to check each repro on a phone before
it is fixed. **Confirmed** means the code path was followed end to end; **suspected**
means the outcome rests on browser, Avalonia or AvaloniaEdit behavior not verified
here. Line numbers are as of that commit. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last item.

A touch screen is a Windows tablet as much as a phone in the web editor, and most of
these hold for both; the two marked **page only** are the browser's.

Every item but 4, 6 and 9 is fixed and gone from here; those keep the numbers they were
first listed under.

## Out of reach

### 4. Sound may never start on an iPhone (suspected, page only)

`src/Flyback.Editor.Web/wwwroot/speakers.js:57` resumes the AudioContext on
`pointerdown` and `keydown` only. A touch `pointerdown` is not a user activation;
`pointerup`/`touchend`/`click` are. Chrome resumes from the second tap, WebKit
never. The web viewer uses `click` (`src/Flyback.Viewer.Web/wwwroot/main.js:655-656`).
Add `pointerup` and `touchend` to the list.

Repro: open the editor on an iPhone and tap around; `flyback.sound().context.state`
stays `"suspended"`.

### 6. A MIDI In preset can't be played (confirmed)

The computer keyboard is a MIDI In's only instrument on the page
(`EditorView.cs:207`). Wants an on-screen keyboard, not a fix.

## On-screen keyboard

### 9. The keyboard covers the field, and iOS sticks zoomed in (suspected, page only)

`src/Flyback.Editor.Web/wwwroot/index.html:5` is `width=device-width, initial-scale=1`.
Chrome Android resizes only the visual viewport for the keyboard, and Avalonia sizes
itself off `#out`, so a box in the lower half is typed into blind:
`interactive-widget=resizes-content` fixes it. Avalonia's hidden
`.avalonia-input-element` has no font size, so iOS zooms in on focus, and
`touch-action:none` (lines 8-10) stops a pinch back out: `maximum-scale=1` or
`.avalonia-input-element { font-size: 16px }`.

Repro: tap a text box low on the screen.

## Elsewhere

### Minor

Every toolbar and inspector button is a glyph labeled only by its tooltip
(`ToolbarButtons.cs:28-31`); so are the socket help (`Inspector.cs:509`), the
disabled Keep-group reason (640-653) and the "Double-click to rename" hints
(803-806, 917; `ControlsPanel.cs:278`). A finger never hovers, and holding long
enough for the tip clicks the button on the way up.
