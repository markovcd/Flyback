# A Line In in the web editor and the web viewer

Planned on 2026-10-06, on `main` at `700fd3f4`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

A Line In plays what the microphone hears in the page, in the editor at `/editor/` and the
viewer at `/viewer/`, as it does on a Linux desktop (ADR-0178). Nothing here was run; it is
read from the code.

## The shape

The sound already renders in a worker, ahead of the speaker, and an AudioWorklet plays it
(ADR-0160, ADR-0162). The microphone has to reach that worker.

- **The page.** `getUserMedia({ audio: { echoCancellation: false, noiseSuppression: false,
  autoGainControl: false } })`, into the worklet. Both `main.js` (viewer) and `speakers.js`
  (editor) build the `flyback-queue` node with `numberOfInputs: 0`; it needs an input, and
  `Queue.process` in `src/Flyback.Viewer.Web/wwwroot/sound.js` posts what it hears to the
  worker over `feed`.
- **The worker.** `speaker.js` writes each chunk into the heap and calls a new `WebExports`
  method that hands it to a `LineInFeed` set as `AudioRenderer.Input` in `WebSound`.
- **The script.** `JsEmitter` renders a whole buffer in one loop, so `line-in/left` and
  `line-in/right` would be frozen for it. Pass a per-frame input buffer to `render` and write
  the two live values at the top of each frame; `JsSound.Render` fills the buffer from
  `renderer.Input`. The interpreter path already works through `AudioRenderer.Input`.
- **Asking.** The worker reports whether the playing program reads `LineInSignal.Left` or
  `Right` in its status. The page asks for the microphone then, after a gesture, and lets it
  go when the sound stops. A refusal is said in the status line.
- **The frame.** The landing page frames the editor, so that `<iframe>` needs
  `allow="microphone"`. The Worker sets no Permissions-Policy, so nothing blocks it there.
- **The Sound tab** already draws no input form in a page (`host.InPage`), so the browser's
  default input is the only choice.

## Caveats

- **Latency.** The worker renders 100 to 250 ms ahead, so the microphone reaches the speakers
  about that late plus the capture. Fine for echoes, filters and a picture driven by a voice;
  not for monitoring. Shorten the queue while a Line In is present.
- **Firefox** may refuse a microphone whose rate differs from the context's.
- **Testing.** The engine and the script can be checked in Node with a `--input` on
  `hear.mjs`, as `render` has one, and a parity test beside `JsProgramTests`. The permission
  and the worklet need a real browser with a microphone, or Chrome's
  `--use-fake-device-for-media-stream --use-file-for-fake-audio-capture=<wav>`.
