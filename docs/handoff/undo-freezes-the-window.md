# An undo on a large patch freezes the window for two seconds

Reported on 2026-09-25, on Windows, with Mycelium open: Ctrl+Z stops the whole
window, the canvas included, for about two seconds. An edit does not. Measured in a
Linux cloud session, which has no GPU and no sound device and could not reproduce
it. The cause is still open. It is on TODO.md; take it off there, and delete this
file, in the commit that lands the fix.

## What is ruled out

Mycelium, with the shipped plugins installed as the catalog (`NodeCatalog.Install`,
or a compile misses the plugin modules and reads far smaller):

| | Edit (one wire out) | Undo |
|---|---|---|
| Picture shader | 59,591 characters, new text | the text exactly as before the edit |
| Sound | 1,727 ops, IL built in 44-59 ms | IL of that shape already built, attached at once |
| UI thread, `history.Undo()` | 10-50 ms, 120-220 ms to settle | 13-45 ms, 130-215 ms to settle |
| UI thread, a real Ctrl+Z press | 30-50 ms | 200-320 ms, reprint and layout included |

So on the UI thread an undo is a hitch, not a freeze. The shader and the sound cost
nothing on an undo:

- `GpuFrameRenderer` keeps the last eight programs it showed, keyed by their text
  (commit `9b0a6f2`), and an undo's text is identical to the one it goes back to, so
  no link is asked of the driver.
- `IlCompiler` keeps built IL by shape, and the undone sound's shape is found.

Measured headless (`UiTest.NewMainWindow` with `EditorSetup.Plugins` loaded from
`tests/Flyback.Plugins.Tests/bin/Release/net10.0/plugins`), and with the real
`GpuPreviewSurface` under Xvfb and Mesa's llvmpipe, which links Whole band's shader
in under 50 ms and so cannot show a link stalling.

## What is left

What the headless run does not have:

- **The render thread on ANGLE.** `GpuPreviewSurface.OnOpenGlRender` draws the whole
  window, so a GL call blocking there freezes the canvas too. Nothing on an undo
  should reach the driver since `9b0a6f2`, but that is reasoning, not a measurement.
- **The sound device.** While the sound plays, the picture's clock is the sound's
  (`Transport.Start`: `surface.Clock = () => audio.Time`), and scopes and meters are
  fed from it. A sound that falls behind stops the picture's clock, and the picture,
  the scopes, the meters and the time readout all stop together, which reads as a
  frozen window.
- Anything else Windows-only on the path of an undo.

## Asked of the user, not yet answered

1. Does the sound play on through the freeze, or stall too? Playing on points at the
   render thread; stalling at the whole process.
2. Does it freeze with playback paused?
3. On every undo and redo, or only the first undo after an edit?
4. Is the build at `9b0a6f2` or later? Before it, an undo relinks the shader.

## How to find it

On Windows, with Mycelium open and playing:

1. Time each step that could block, and print any over 100 ms: `SetPatch`, `Render`
   and the `DrawPatch` inside it in `GpuFrameRenderer`, `Document.Undo`,
   `Transport.Load`, `AudioEngine.Update`, and the sound device's callback. One undo
   names the step. Take the timing out again before committing the fix.
2. If nothing prints and the window still stops, the stall is outside these: take a
   `dotnet-stack` or a `dotnet-trace` of the process during the freeze.

## Also to correct

ADR-0035's amendment of 2026-09-24 says relinking froze the preview on an undo
through ANGLE. That was a guess, and the measurements above leave it doubtful.
Rewrite that sentence to the cause once it is known.
