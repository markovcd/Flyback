# Looking at a patch

## If the picture or the sound is all you need, use the viewer

`flyback-viewer` is the way to see or hear a patch. It opens the patch, plays picture and sound at once and writes nothing. Do not launch the editor for it, and do not render a clip to look at ten seconds of it.

```bash
flyback-viewer nebula.fbk --mute --for 10
flyback-viewer --preset "Dub" --size 720p --background
flyback-viewer --preset "Whole band" --hidden --for 10
```

`flyback-cli viewer <same arguments>` runs the same program, and `flyback-viewer --help` lists every flag with this machine's defaults.

**Why:** the editor loads the whole editor and keeps settings, layout and a recovery file, and `flyback-cli render` of a clip takes minutes. The viewer opens at once and leaves nothing behind.

**How to apply:**

- **Sound only:** `--hidden --for <seconds>` opens no window, so nothing lands on the user's screen. `--mute` keeps the clock and drops the speakers, which is the flag for a look that should not be heard.
- **Picture:** `--background` opens the window without taking focus, `--mute` silences it, and `--for <seconds>` closes it. Size the picture with `--size 720p` (or `WxH`) and start later with `--from <seconds>`.
- **A still frame:** use `flyback-cli render -o shot.png --at <seconds>`, which is quick. The viewer writes no frames.
- **Anything about the editor itself** (the canvas, the inspector, the panel, a shortcut) still needs the real window, by `running-the-app.md`. The viewer shows what a patch does, not how it is edited.
- A patch that plays sound plays through the user's speakers the moment the viewer opens. Pass `--mute` unless the sound is what is being checked.
- Run from the publish folder, not the viewer's own build output: the sound backends are plugins the shell lays out, and without them the viewer says it has no sound.
- The viewer never writes to `%APPDATA%\Flyback`. Everything it changes is its own window, and `running-the-app.md`'s rule about another Flyback being open does not apply to a `--hidden` run, which has no window.
