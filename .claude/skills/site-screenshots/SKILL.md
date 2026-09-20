---
name: site-screenshots
description: Use when a UI or preset change makes a screenshot in site/assets/shots stale and it needs retaking from the real Flyback app without a human at the keyboard - the PowerShell window-capture recipe and its coordinate quirks.
---

# Recapturing site screenshots

The full-window shots in `site/assets/shots` (nebula.webp, whole-band.webp, euclid-kit.webp, plasma*.webp, tutorial-canvas.webp; 1600x863) are the maximized app with a saved preset opened from the command line (`Flyback.exe nebula.fbk`), patch framed, caught at a chosen `t`. A preset dumped with `PatchIO.ToJson` from a scratch test is the `.fbk`; set every `Group.Collapsed` in the dump for the shots that show shut boxes (whole-band, euclid-kit). tutorial-canvas.webp is the tutorial's section-6 text saved as `t5.fbks`. The website has to stay accurate, so a stale screenshot is retaken in the same commit as the UI change.

A preset with sound plays through the user's speakers while it is captured, for the few seconds the recipe takes. Capture it anyway; muting the system is a system setting and off limits.

## Recipe (Nebula, PowerShell)

1. Follow `.claude/rules/running-the-app.md`: wait for any other Flyback to close, then `Start-Process -PassThru` the Debug `Flyback.exe` with the `.fbk`, drive only that process's `Id` from here on, and mark the window red while you are driving it.
2. `ShowWindow` maximize.
3. `PostMessage` a left click on bare canvas (focus).
4. `SendKeys ^f` (frame).
5. `PostMessage` one `WM_MOUSEWHEEL` notch out for margins (Nebula, Whole band; Plasma, Euclid kit and the tutorial are framed without it).
6. Click Rewind, then sleep until `t` is about 6.5 s (that is where Nebula is orange and magenta like the hero image; the others are caught at about 8 s).
7. Put the title bar back to its own color and title, then `PrintWindow(hwnd, dc, 2)`, cropped to the DWM extended frame bounds. The crop keeps the title bar, so the red marking and the danger title are in the shot until they are cleared.
8. Kill the process, then PIL resize 2560x1380 -> 1600x863 and save webp at quality 88.

## Coordinates and popups

- Call `SetProcessDPIAware()` in the PowerShell script before anything else. Without it the script sees the window at its scaled-down 2062x1118 size and `PrintWindow` cuts off the right and the bottom; with it, the maximized window is 2578x1398 with a 2560x1380 frame at (9, 9), which is what is cropped.
- Posted clicks are in **client** coordinates, physical pixels, and the client area starts under the native title bar: the toolbar's first button is about (35, 30), Rewind about (589, 30) and `{ }` about (398, 30). From a 1600-wide crop, client is about (x x 1.6, y x 1.6 - 29).
- `WM_MOUSEWHEEL` takes screen coordinates, so turn a client point into one with `ClientToScreen` before posting it; a negative notch count zooms in.
- A dropdown is a separate top-level popup window of the same process, so `PrintWindow` on the main handle misses it. `EnumWindows` for the process's other visible windows, `PrintWindow` each, and draw it onto the main capture at its screen offset (how presets-list.webp was made, list open over Kaleidoscope).
- A `.fbks` opens in the text view; the `{ }` toolbar button switches to the canvas, since a sent F2 did not.
- A posted `WM_MOUSEWHEEL` does not scroll the inspector, so a row below the fold is checked with a headless `UiTest` instead (see `ExpressionInspectorTests`).
- preview-*.webp stills (888x500) of chart presets can come from `flyback-cli render` to .avi plus an ffmpeg frame grab, since an export fills Scopes and Analyzers.
