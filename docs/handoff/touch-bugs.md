# Touch bugs in the web editor on a phone

Audited on 2026-09-30, on `main` at `b5003a2e`, by reading the code; nothing here has
been run on a device yet, and the user means to check each repro on a phone before
it is fixed. **Confirmed** means the code path was followed end to end; **suspected**
means the outcome rests on browser, Avalonia or AvaloniaEdit behavior not verified
here. Line numbers are as of that commit. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last item.

The fix for the module list, which threw up the on-screen keyboard on opening, is
`b5003a2e`: `CanvasGestures.ByFinger`, `ModuleAsked(ByFinger)` and
`ModulePalette.Reset(typing)`. Items 7 and 8 want the same treatment.

Fix 1–3 and 10 first: they make the page unusable on a phone.

## Out of reach

### 1. Most of the toolbar is off-screen in portrait (confirmed)

`Toolbar.cs:261` lays the whole bar in one horizontal StackPanel, no wrap or
scroll; the page's bar is about 1,000 px (34 px buttons, `ToolbarButtons.cs:43`;
`SeekBar.cs:50` track 180, `:63` length box 76; `VolumeSlider.cs:46` 90). Pause,
Rewind, Seek, Length, Loop and Volume are never on a 390 px screen, and the
transport overlay is full-screen only (`FullScreenPreview.cs:211`), which the page
turns off (`ShellLayout.cs:280`). `Toolbar.On(Touched)` (290-294) then inserts Add
and Frame before Undo (221-225), pushing Swap and Side off as well.

Repro: open the web editor in portrait, look for Pause or Volume; touch the canvas
once and watch Swap and Side slide off.

### 2. The inspector's action buttons are off-screen in portrait (confirmed)

`ShellLayout.cs:134` and `:136` give the canvas column `MinWidth=280` and the side
column `MinWidth=300`, 585 px together; the side column is shown by default
(line 59). `Inspector.cs:524-533` right-aligns `ActionRow`, so Delete, Duplicate
(ADR-0165's touch button), Group, Switch and Ungroup are in the hidden part.

Repro: tap a module, look for Delete or Duplicate.

### 3. The status line is zero wide (confirmed)

`StatusBar.cs:81` has columns `*,Auto,Auto,Auto`; the counters (`:139-141`, about
550 px) sit in an Auto column, measured at infinite width, so their `TextTrimming`
(line 51) never trims and the star column holding `ReportLine` gets nothing.
Compile errors, "Added X" and "Could not open…" are never seen, nor the log they open
(`ReportLine.cs:37`, 440 px wide).

Repro: add a module or break a formula; nothing is said.

### 4. Sound may never start on an iPhone (suspected)

`src/Flyback.WebEditor/wwwroot/speakers.js:57` resumes the AudioContext on
`pointerdown` and `keydown` only. A touch `pointerdown` is not a user activation;
`pointerup`/`touchend`/`click` are. Chrome resumes from the second tap, WebKit
never. The web viewer uses `click` (`src/Flyback.Web/wwwroot/main.js:655-656`).
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

### 7. The preset gallery focuses its search box on open (confirmed)

`Gallery/PresetGallery.cs:651` posts `Box.Focus()` on every attach, whatever
opened it. `KeepCard` (line 258) does the same, but only after an explicit tap and
never in the page.

Repro: tap the Presets button; the keyboard covers the tiles.

### 8. The Code button focuses the text view (suspected)

`Document.cs:1072-1074`: `ShowCode(true)` calls `source.Focus()`
(`SourceView.cs:592`, `text.TextArea.Focus()`); the text is always editable
(`Document.cs:1444`). Suspected only in whether AvaloniaEdit's TextArea is a
text-input client the browser backend raises the keyboard for.

Repro: tap `</>`.

### 9. The keyboard covers the field, and iOS sticks zoomed in (suspected)

`src/Flyback.WebEditor/wwwroot/index.html:5` is `width=device-width, initial-scale=1`.
Chrome Android resizes only the visual viewport for the keyboard, and Avalonia sizes
itself off `#out`, so a box in the lower half is typed into blind:
`interactive-widget=resizes-content` fixes it. Avalonia's hidden
`.avalonia-input-element` has no font size, so iOS zooms in on focus, and
`touch-action:none` (lines 8-10) stops a pinch back out: `maximum-scale=1` or
`.avalonia-input-element { font-size: 16px }`.

Repro: tap a text box low on the screen.

## Canvas

### 10. Zoomed out, a finger can't select or move a module (confirmed)

`Fingers.Socket` (`Fingers.cs:251-259`, used by the press at 137, the hold at 217
and the tap at 222) snaps to the nearest socket within `Reach / view.Zoom` graph
units, `Reach = 16` screen px. A module is 196 units wide with sockets down both
edges (`NodeGeometry.cs:41-47`); at zoom 0.15 (a framed preset on a phone; `MinZoom`
is 0.13) the whole module is within reach of a socket, and `HitPort` beats
`HitNode` in `CanvasGestures.Pressed`. A tap starts and ends a wire, a drag draws
one, a hold dials or does nothing (`CanvasGestures.cs:313`), never flips. Shut boxes
too. Cap the reach in graph units, or skip the snap when the point is on a module's
body.

Repro: open a preset, so it is framed; tap or drag a module a third of the way in
from its left edge.

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

### 19. The empty-selection help assumes a mouse and keys (confirmed)

`Inspector.cs:190-194` always shows `InspectorHelp.Canvas`
(`Inspect/InspectorHelp.cs:15-44`): right-click, Space, Open, Save, Ctrl+R, F2 for
the locked text (52-62). Nothing on holding a finger, two-finger pan and pinch, or
the Add, Frame and Duplicate buttons; it looks at neither `setup.InPage` nor
whether a finger has been used.

### 20. The unsaved-changes dialog overflows a 360 px screen (confirmed; the Save part suspected)

`Files/UnsavedWork.cs:229-249`: the buttons need about 328 px, the dialog gives
about 238 (`ModalOverlay` `Inset=40`, line 25), so Cancel is cut off. `offerSave`
defaults to true, so the page offers "Save…", which goes to
`SaveFilePickerAsync` (`WindowFilePickers.cs:13`), missing from mobile browsers.

Repro: edit a patch, then pick another preset.

### Minor

Every toolbar and inspector button is a glyph labeled only by its tooltip
(`ToolbarButtons.cs:28-31`); so are the socket help (`Inspector.cs:509`), the
disabled Keep-group reason (640-653) and the "Double-click to rename" hints
(803-806, 917; `ControlsPanel.cs:278`). A finger never hovers, and holding long
enough for the tip clicks the button on the way up.
