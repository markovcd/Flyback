---
name: site-screenshots
description: Use when a UI or preset change makes a screenshot in site/assets/shots stale and it needs retaking from the real Flyback app without a human at the keyboard - the PowerShell window-capture recipe, its coordinate quirks, and why presets with sound are left to the user.
---

# Recapturing site screenshots

The full-window shots in `site/assets/shots` (nebula.webp, whole-band.webp; 1600x863) are the maximized app with a saved preset opened from the command line (`Flyback.exe nebula.fbk`), patch framed, caught at a chosen `t`. A preset dumped with `PatchIO.ToJson` from a scratch test is the `.fbk`. The website has to stay accurate, so a stale screenshot is retaken in the same commit as the UI change.

**Presets with sound are not captured unattended.** Such a preset plays through the user's speakers the moment it opens and the app has no mute flag. Muting the system is a system setting and off limits, so leave those shots (Whole band's) to the user. This recipe is for picture-only presets.

## Recipe (Nebula, PowerShell)

1. `Start-Process` the Debug `Flyback.exe` with the `.fbk`.
2. `ShowWindow` maximize.
3. `PostMessage` a left click on bare canvas (focus).
4. `SendKeys ^f` (frame).
5. `PostMessage` one `WM_MOUSEWHEEL` notch out for margins.
6. Sleep until `t` is about 6.5 s (that is where Nebula is orange and magenta like the hero image).
7. `PrintWindow(hwnd, dc, 2)`, cropped to the DWM extended frame bounds.
8. Kill the process, then PIL resize 2560x1380 -> 1600x863 and save webp at quality 88.

## Coordinates and popups

- Posted clicks are in **client** coordinates, and the client area starts under the native title bar, so the toolbar's first button is about (35, 30) physical pixels on the maximized 2560-wide window, not (35, 59).
- An uncropped `PrintWindow` capture of the maximized app is 2062x1118 while posted client coordinates run about 0.79x of it: client = ((shot - 7) x 0.79) across, ((shot - 28) x 0.79) down.
- A dropdown is a separate top-level popup window of the same process, so `PrintWindow` on the main handle misses it. `EnumWindows` for the process's other visible windows, `PrintWindow` each, and draw it onto the main capture at its screen offset (how presets-list.webp was made, list open over Kaleidoscope).
- A `.fbks` opens in the text view; the `{ }` toolbar button (client about 318, 32) switches to the canvas, since a sent F2 did not.
- A posted `WM_MOUSEWHEEL` does not scroll the inspector, so a row below the fold is checked with a headless `UiTest` instead (see `ExpressionInspectorTests`).
- preview-*.webp stills (888x500) of chart presets can come from `flyback-cli render` to .avi plus an ffmpeg frame grab, since an export fills Scopes and Analyzers.
