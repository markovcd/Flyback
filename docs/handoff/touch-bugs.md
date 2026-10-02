# What a finger still cannot do

Audited on 2026-09-30, on `main` at `b5003a2e`, by reading the code; nothing here has
been run on a device yet, and the user means to check each repro on a phone before
it is fixed. **Confirmed** means the code path was followed end to end; **suspected**
means the outcome rests on browser, Avalonia or AvaloniaEdit behavior not verified
here. Line numbers are as of that commit. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last item.

A touch screen is a Windows tablet as much as a phone in the web editor, and most of
these hold for both; the two marked **page only** are the browser's.

Items 1, 2, 3, 7, 10, 19 and 20 are fixed and gone from here; the rest keep the numbers
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

### 5. Keys with no button (confirmed)

Copy, cut, paste and select-all are keys only (`NodeEditor.cs:203-209`); only
duplicate got a button. Escape is the only way out of a lifted wire, a band or a
carry (`NodeEditor.cs:186`), and the only way to cancel a rename
(`Inspect/NameBox.cs:90-104`, `ControlsPanel.cs:439-448`, `InspectorRows.cs:141-152`;
losing the focus keeps the new name). The code view's text size is Ctrl+wheel or
Ctrl+/- only (`Controls/SourceView.cs:148`, `:288-305`). Tidy's "only selected"
reads Ctrl at the press (`Toolbar.cs:176`).

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

### 11. A dropped wire snaps to a socket of the same kind and vanishes (confirmed)

`Fingers.Wiring` (`Fingers.cs:262`) → `CanvasScene.NearestSocket`
(`CanvasScene.cs:375-394`) ignores input versus output; `CompleteWire` returns
quietly on a same-kind pair (`CanvasGestures.cs:920`). A wire lifted off an input
has already been recorded as lifted (861-865), so it is deleted. Prefer sockets of
the opposite kind and skip the wire's own.

Repro: two modules side by side with a small gap; drag a wire from a third module's
output and let go in the gap, a little nearer the left one's outputs. No wire.

### 12. A two-finger pan can edit the patch (confirmed)

Once the first finger passes 10 px, `Fingers.cs:132-139` presses Left; the second
finger then pans on top (116-118), and lifting the first ends in `Released(Left)`
with `drag == Pan`, which drops a suspended wire (`CanvasGestures.cs:544-549`) and
records a suspended move (547).

Repro: land one finger on a patched input a moment before the other and pan; the
wire comes off. On a module, the module moves and it is an undo step.

### 13. A held-finger dial jumps on its first move (confirmed)

`Fingers.cs:217` hands `Pressed(Right)` the snapped point, which `SocketDial.Start`
keeps as `dialLast` (`SocketDial.cs:64`); the moves after it are the raw finger
(`Fingers.cs:146-147`), so the gap (up to 16 px, 10% of the range) is turned on the
first move (`SocketDial.cs:83`). `FingersTests` offsets the finger only
horizontally, which is why it passes.

Repro: hold about 12 px below Sine's frequency socket until the dial shows, then
wiggle slightly.

### 14. Holes in the finger state machine (confirmed)

- Lifting one finger of a pinch makes the phase Spent (`Fingers.cs:187-189`); a
  finger put back does nothing until both are up.
- A held finger (Right phase) ignores a second one: `Down` has no Right case
  (109-120). A pinch whose second finger comes after 450 ms is a palette, a flip or a
  dial.
- A third finger is ignored on Down (105) but its Up matches `Phase.Left when
  panning` (177) and ends the pan; in a pinch it ends the pinch (187).
- `Lost` for the second finger calls `gestures.CaptureLost` (203), ending the first
  finger's drag too.

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

### 17. The step list inserts on a scroll (confirmed)

`Inspect/StepList.cs:208-245`: the 6 px strip between rows (`InsertHeight`, line 30)
inserts on `PointerPressed`, and is shown only on hover. The reorder handle
(421-461) has no `PointerCaptureLost`, so a scroll that steals it leaves the row
shifted at 0.8 opacity with `dragging` set.

Repro: select a sequencer and scroll starting between two rows.

### 18. A tapped grid cell rarely toggles (confirmed)

`PartGrid.cs:31` has `Slop = 3` px for every pointer (Fingers uses 10); past it
`held.Moved` is set (254-270) even when the value clamps unchanged, and `LetGo`
(300-301) refills instead of toggling. A jittered first tap never counts toward the
double-tap glide.

Repro: tap a lit cell in an Arrangement's grid a few times.

### Minor

Every toolbar and inspector button is a glyph labeled only by its tooltip
(`ToolbarButtons.cs:28-31`); so are the socket help (`Inspector.cs:509`), the
disabled Keep-group reason (640-653) and the "Double-click to rename" hints
(803-806, 917; `ControlsPanel.cs:278`). A finger never hovers, and holding long
enough for the tip clicks the button on the way up.
