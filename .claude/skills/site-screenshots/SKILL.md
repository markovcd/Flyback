---
name: site-screenshots
description: Use when a UI or preset change makes a screenshot in site/assets/shots stale, or when the site needs a new picture of a patch or a module - the headless shot tests for canvas-only crops, and the PowerShell window-capture recipe and its coordinate quirks for the full-window ones.
---

# Recapturing site screenshots

## A stale full-window shot is listed, not retaken on the spot

A change that makes a full-window shot stale does not stop to drive the real window. It adds the shot to [stale.md](stale.md) beside this skill, in the change's own commit: the file, where the site uses it, what no longer matches, and the date. The site's prose is still fixed in that commit; only the picture waits.

Each time a shot is added, look at the whole list. Once it holds **four or more shots**, or any shot the front page leads with (the hero, nebula.webp), propose to the user spinning a second session to retake the lot, with the background-task chip carrying the list as its prompt. That session works through this skill's recipe, takes each retaken shot off the list, and lands them on `main` in one commit.

**Why:** the user does not want a UI change stalled on a minutes-long window-driving capture, and one session retaking several shots costs about what one shot does.

The canvas-only shots below are a test run, not a window, so those are still retaken in the change's own commit.

## The canvas-only shots come from a test, not a window

`patch-*.webp` (the patch figures) and `skin-*.webp` (the plugin guide's backgrounds) are headless captures of a real `NodeEditor`, cropped to the modules. Nothing here is driven by hand:

```bash
SHOT_DIR=<somewhere> ./tests/Flyback.Editor.Tests/bin/Release/net10.0/Flyback.Editor.Tests.exe -method "*PatchShotTests*"
```

`SkinShotTests` for the other set. Both skip without `SHOT_DIR`. Convert the PNGs to webp at quality 88 and copy them in, keeping the `width`/`height` attributes on the site's `<img>` in step with the new pixel size. A new patch figure is a new `.fbks` in `PatchShotTests`, never a drawing — see ADR-0119.

The shipped module plugins' embedded previews (`src/Flyback.Plugins.<Name>/preview.webp`) come from `PluginPreviewShotTests` the same way: four of each plugin's modules in a row. Retake them when one of those modules gains a port, a glyph or a skin. The plugins with no modules (sound and MIDI, key stores, assistants) have drawn previews instead, from `PluginArtShotTests`.

## The full-window shots

The window with a patch open, framed, a box selected and the picture at a chosen second is one command, with no window opened and nothing heard (ADR-0166):

```bash
flyback-cli shot --preset "Flyback Theme" -o theme.png --at 30.3 --size 1440x900 --select "Picture: Scope"
```

Run it from a folder holding the CLI, the editor and the plugins together (a publish, or the editor's build output with `flyback-cli`'s build copied in). A shot has no title bar, says CPU on the status bar, and shows no popup, dropdown or dialog; a picture that needs any of those comes from the real window, by the recipe below.

flyback-theme.webp and plasma.webp (1440x900) are that command as it stands, Plasma at `--at 55.7` with nothing selected. The rest are 1440x900 shots cropped to one part of the window (toolbar to row 66, canvas to 935 across, right column from 941, status bar from the bottom's last 28 rows): beat-canvas and echoes-canvas are each tutorial's `data-finished` listing with `--canvas`, cropped to the canvas and knob panel (beat at `--size 1440x820`, echoes at `1440x720`, so the patch fills it); plasma-code is index.html's '# The same patch, as text.' listing, cropped above the inspector; plasma-inspector is `--preset Plasma --select Sine`, the right column; tutorial-text is t5.fbks `--at 4.4 --select slow`, everything between toolbar and status bar; knob-panel is Slow weather's panel row; whole-band and euclid-kit are their bundles with every group shut, `--crop`. tutorial-canvas.webp is the tutorial's section-6 text saved as `t5.fbks` and shot with `--at 8 --crop`, which shows the canvas rather than the text and keeps only the modules with 24 px around them.

The full-window shots in `site/assets/shots` (nebula.webp, whole-band.webp, euclid-kit.webp, the other plasma-*.webp; 1600x863) are the maximized app with a saved preset opened from the command line (`Flyback.exe nebula.fbk`), patch framed, caught at a chosen `t`. `flyback-cli pack --preset <name> -o <name>.fbkb` writes the preset out; for the shots that show shut boxes (whole-band, euclid-kit), set every `Group.Collapsed` in the bundle's `patch.fbk`, since a bundle is a zip. A stale one goes on the list above rather than being retaken in the UI change's commit.

A preset with sound plays through the user's speakers while it is captured, for the few seconds the recipe takes. Capture it anyway; muting the system is a system setting and off limits.

## Recipe (Nebula, PowerShell)

1. Follow the `running-the-app` skill: wait for any other Flyback to close, then `Start-Process -PassThru` the Debug `Flyback.exe` with the `.fbk`, drive only that process's `Id` from here on, and mark the window red while you are driving it.
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
- preview-*.webp stills (888x500) are `flyback-cli render --at`. A patch that keeps its last frame (Trails, Feedback: Marble, Nebula, Feedback tunnel, Echoes) needs a clip and an ffmpeg frame grab instead, and a chart preset (Waveform) a clip with `--processor`, since a clip drawn on the GPU leaves Scopes and Analyzers flat.
