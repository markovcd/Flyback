# ADR-0162: The editor runs in a browser, with the picture on a canvas of its own

**Status:** Accepted · 2026-09-29 · *user-directed* · builds on
[0160](0160-a-patch-plays-in-a-browser-on-the-engine-compiled-to-webassembly.md) and
[0150](0150-the-editor-is-composed-in-a-container.md) · the page edits and draws; its
sound is not implemented yet

## Context

The web viewer ([0160](0160-a-patch-plays-in-a-browser-on-the-engine-compiled-to-webassembly.md))
plays a patch in a page. Editing one there raised three questions the viewer did
not answer, and a spike measured them in Chromium on builds compiled ahead of time:

- **Whether the editor's window runs at all.** Under Avalonia.Browser the real
  `NodeEditor`, built from its own container, draws a preset and takes the wheel and
  the pointer. Zooming Whole band (162 modules, 273 wires) draws a frame in 21 ms
  at the median.
- **What an edit costs.** Applying one to Whole band through `CanvasHistory`, with
  every reaction and the picture's recompile, takes 9 to 15 ms on the page's
  thread. The sound's half (recompile, `JsEmitter`, a new script) takes 25 to 65 ms,
  and the new script renders at full speed from its second buffer: an edit pays
  once, and never warms up again.
- **Where the picture draws.** `GpuPreviewSurface` is an `OpenGlControlBase`, which
  Avalonia.Browser gives no context: it waits, gives up and falls back.

The editor was also a program rather than a library: `Flyback.App` is an executable
on `Avalonia.Desktop`, and a page can reference one only by turning off the SDK's
check that it should not.

## Decision

**The page is one runtime, holding the editor and the picture.** Avalonia.Browser
draws the window; the picture is compiled where the patch is edited, with nothing
serialized between them.

**The picture draws on a canvas of its own.** A `NativeControlHost` places an HTML
`<canvas>` where the preview is in the layout, and the page hands `PreviewHost` a
GPU surface of its own, `CanvasPreview`, which draws into it with the viewer's
`WebGl` and `GpuFrameRenderer`. The rest of the editor talks to `IPreviewSurface`
and does not know.

**The editor is a control a window or a page holds.** `EditorView` lays out the
regions and answers the keys and dropped files of whichever top level it is handed;
`MainWindow` is the desktop's window around it. Title, focus and close are the
container's `ITitle`, `IFocus` and `IClose`, which the page registers as its own.

**The preset site serves it at `/editor/`, beside the viewer at `/viewer/`**, whose
`gl.js` the page imports and whose worker will play its sound. The site publishes
it beside itself unless built with `-p:WebEditor=false`.

**The landing page shows it where it shows the editor's photograph.** The hero's
screenshot of Slow weather carries a button that swaps it for the page in a frame,
opened on the same preset with `?preset=`. Nothing loads until it is pressed; a
screen narrower than 900 pixels opens the page in a tab instead.

**The sound plays in a worker, on the viewer's build.** The worker needs the engine
and the plugins, not Avalonia, so it loads the 7 MB viewer rather than the editor.
Each edit posts the patch's bytes; the worker keeps only the latest, so a knob
dragged through fifty values compiles as often as it can keep up, and it clears the
speaker's queue when a new program starts, so the edit is heard in about a tenth of
a second rather than after a quarter second of the old one.

**The editor is a library, `Flyback.Editor`.** Its window, canvas, regions, the
container that composes them, and what they read (usage, release notes, the running
version and the key a release is checked with) move there. The namespaces stay
`Flyback.App.*`, as they did for `Flyback.Ui` ([0124](0124-what-two-shells-draw-with-is-a-project-of-its-own.md)).
`Flyback.App` is the desktop program around it: `Program`, `FlybackApp`, `Startup`,
installing an update, and the plugins shipped in its folder. A browser page is
another program around the same library, and registers its own services in the
container where the desktop's cannot work: the preview, the sound, settings and
files.

**Left out of the page to start with:** the assistant (a model's HTTP from a page
meets CORS, and a key has nowhere safe to live), export, MIDI and installing plugins.
`EditorSetup.InPage` takes open, save, record, the assistant, settings, plugins and
About off the toolbar and their keys, leaves the picture in place on a double-click,
tries no preset the gallery's pointer rests on, and the page hides its canvas while
a dialog is up, since nothing can draw over it.
The plugins that ship and make modules are referenced, as in the desktop editor;
the ones the preset site hosts are added only in the All plugins configuration
([0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)).

## Alternatives considered

**The viewer beside the editor.** Three runtimes instead of two, each holding the
plugins and the patch; every edit written, posted and parsed again for the picture;
and full screen, the resolution and the panel's knobs turned from one container's
state into messages between two programs.

**Reading each frame back into a `WriteableBitmap`**, so Avalonia composites the
picture like any other control and draws over it. About 2 MB a frame at 960×540,
paid on every frame, to keep overlays that can sit beside the preview instead.

## Consequences

- A native control draws above Avalonia's content, so the transport, the stats and
  the stage knobs cannot overlay the page's preview; they sit beside it or become
  HTML.
- The page downloads about 30 MB compressed, against the viewer's 7: Avalonia,
  Skia, the Inter font and SVG.
- The window, the editing and the picture share the page's one thread. The sound
  does not.
- Avalonia.Browser links Skia and HarfBuzz into the runtime, which needs the
  wasm-tools workload, so `Flyback.slnx` lists the page without building it, and a
  machine without the workload builds the site with `-p:WebEditor=false`. The gate's image installs it.
- A browser build of the editor pins the WebAssembly Skia and HarfBuzz natives to
  the managed SkiaSharp the editor resolves; Avalonia.Browser's own are older.
- The release key's public half is `src/Flyback.Editor/Updates/release-key.pem`,
  embedded in the library whose `ReleaseSignature` reads it
  ([0088](0088-a-release-installs-itself-at-the-next-start.md)).
