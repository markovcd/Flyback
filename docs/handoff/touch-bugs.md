# What a finger still cannot do

Audited on 2026-09-30, on `main` at `b5003a2e`, by reading the code; nothing here has
been run on a device yet, and the user means to check each repro on a phone before
it is fixed. **Confirmed** means the code path was followed end to end; **suspected**
means the outcome rests on browser, Avalonia or AvaloniaEdit behavior not verified
here. Line numbers are as of that commit. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last item.

A touch screen is a Windows tablet as much as a phone in the web editor, and most of
these hold for both; the two marked **page only** are the browser's.

Items 1, 2, 3, 5, 7, 10, 11, 12, 13, 14, 17, 18, 19 and 20 are fixed and gone from here; the rest keep the numbers
they were first listed under. Whether the last press was a finger is `LastPress.ByFinger`,
which the module list and the preset gallery read to leave their text box to be tapped;
item 8 wants the same.

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

### 8. The Code button focuses the text view (suspected)

`Document.cs:1072-1074`: `ShowCode(true)` calls `source.Focus()`
(`SourceView.cs:592`, `text.TextArea.Focus()`); the text is always editable
(`Document.cs:1444`). Suspected only in whether AvaloniaEdit's TextArea is a
text-input client the browser backend raises the keyboard for.

Repro: tap `</>`.

### 9. The keyboard covers the field, and iOS sticks zoomed in (suspected, page only)

`src/Flyback.Editor.Web/wwwroot/index.html:5` is `width=device-width, initial-scale=1`.
Chrome Android resizes only the visual viewport for the keyboard, and Avalonia sizes
itself off `#out`, so a box in the lower half is typed into blind:
`interactive-widget=resizes-content` fixes it. Avalonia's hidden
`.avalonia-input-element` has no font size, so iOS zooms in on focus, and
`touch-action:none` (lines 8-10) stops a pinch back out: `maximum-scale=1` or
`.avalonia-input-element { font-size: 16px }`.

Repro: tap a text box low on the screen.

## Canvas

### 15. A tooltip stays after the finger lifts (suspected)

`CanvasGestures.cs:495-499` reaches `tips.Over` and `marks.Hover` from a touch in
the Right phase; the tip comes down only on `PointerExited` (`NodeEditor.cs:170-175`),
which may not come for a lifted finger.

Repro: hold a module until it mutes, slide onto an output socket, lift.

## Inspector and panels

### 16. Scrolling the inspector changes values (suspected in part)

The inspector scrolls (`ShellLayout.cs:296`) over a `Slider` per knob row
(`InspectorRows.cs:352`); Avalonia's Slider moves to the press on any left press, a
finger included. `Knob.cs:95-116` (no slop), `LevelBar.cs:69-72` and
`PartGrid.cs:194-239` turn from the first pixel and call no
`e.PreventGestureRecognition()`. Each jump is an undo step. Which of the scroll and
the control wins past the recognizer's start distance is the suspected part.

Repro: select an oscillator and swipe up starting on a slider or knob.

### Minor

Every toolbar and inspector button is a glyph labeled only by its tooltip
(`ToolbarButtons.cs:28-31`); so are the socket help (`Inspector.cs:509`), the
disabled Keep-group reason (640-653) and the "Double-click to rename" hints
(803-806, 917; `ControlsPanel.cs:278`). A finger never hovers, and holding long
enough for the tip clicks the button on the way up.
