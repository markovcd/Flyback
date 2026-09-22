---
name: site-screenshots
description: Use when a UI or preset change makes a screenshot in site/assets/shots stale, or when the site needs a new picture of a patch or a module - the headless shot tests for canvas-only crops, and the PowerShell window-capture recipe and its coordinate quirks for the full-window ones.
---

# Recapturing site screenshots

## The canvas-only shots come from a test, not a window

`patch-*.webp` (the patch figures) and `skin-*.webp` (the plugin guide's backgrounds) are headless captures of a real `NodeEditor`, cropped to the modules. Nothing here is driven by hand:

```bash
SHOT_DIR=<somewhere> ./tests/Flyback.App.Tests/bin/Release/net10.0/Flyback.App.Tests.exe -method "*PatchShotTests*"
```

`SkinShotTests` for the other set. Both skip without `SHOT_DIR`. Convert the PNGs to webp at quality 88 and copy them in, keeping the `width`/`height` attributes on the site's `<img>` in step with the new pixel size. A new patch figure is a new `.fbks` in `PatchShotTests`, never a drawing — see ADR-0119.

The shipped module plugins' embedded previews (`src/Flyback.Plugins.<Name>/preview.webp`) come from `PluginPreviewShotTests` the same way: four of each plugin's modules in a row. Retake them when one of those modules gains a port, a glyph or a skin.

## The full-window shots

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
- Posted clicks are in **client** coordinates, physical pixels, and the client area starts under the native title bar: the toolbar's first button is about (35, 30), Pause about (589, 30), Rewind about (643, 30), Settings about (833, 30) and `{ }` about (398, 30). From a 1600-wide crop, client is about (x x 1.6, y x 1.6 - 29).
- `WM_MOUSEWHEEL` takes screen coordinates, so turn a client point into one with `ClientToScreen` before posting it; a negative notch count zooms in.
- A dropdown is a separate top-level popup window of the same process, so `PrintWindow` on the main handle misses it. `EnumWindows` for the process's other visible windows, `PrintWindow` each, and draw it onto the main capture at its screen offset (how presets-list.webp was made, list open over Kaleidoscope).
- The module list opens under the real mouse pointer, not where a posted click lands, and a posted right-click often opens nothing. Send Space instead, and pan the canvas (posted middle-drag) to put the patch beside `GetCursorPos` just before it, in one script; the user's mouse moves between separate ones (how palette.webp was made).
- A `.fbks` opens in the text view; the `{ }` toolbar button switches to the canvas, since a sent F2 did not.
- A posted `WM_MOUSEWHEEL` does not scroll the inspector, so a row below the fold is checked with a headless `UiTest` instead (see `ExpressionInspectorTests`).
- preview-*.webp stills (888x500) of chart presets can come from `flyback-cli render` to .avi plus an ffmpeg frame grab, since an export fills Scopes and Analyzers.
