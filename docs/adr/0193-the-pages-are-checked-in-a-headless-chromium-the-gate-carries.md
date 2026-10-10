# ADR-0193: The pages are checked in a headless Chromium the gate carries

**Status:** Accepted · 2026-10-10 · implemented in `tests/Flyback.Specs/Support/`
(`Chromium`, `BrowserPage`, `PageServer`, `StaticAssets`), `BrowserSteps.cs`,
`PagesInABrowser.feature` and the Dockerfile

## Context

The web viewer and the web editor ([0160](0160-a-patch-plays-in-a-browser-on-the-engine-compiled-to-webassembly.md),
[0162](0162-the-editor-runs-in-a-browser-with-the-picture-on-a-canvas-of-its-own.md)) are a WebAssembly build, page scripts, a
worker, WebGL and Web Audio, and only the first was tested. `hear.mjs` runs the
viewer's sound under Node; the page scenarios drive the desktop editor headless; the
viewer's `main.js` was read as text. Nothing reached `window.flyback`, a tap, the
browser holding sound back, the microphone or Android's audio element.

## Decision

**The specs open both pages in Chrome for Testing's headless shell.** The gate's image
downloads it pinned by version and SHA-256; Ubuntu's chromium is a snap and does not
run in a container. Locally a scenario finds it through `FLYBACK_CHROMIUM` or by name
on the path, and skips where there is none; in the gate a missing browser fails
(`Needs.Tool`).

**The DevTools protocol is spoken directly.** `BrowserPage` is a `ClientWebSocket`
and a handful of methods: navigate, evaluate, touch, drag, a user agent, the console
and media players. Playwright and Puppeteer would be a package, a driver and a browser
download of their own for that much.

**The pages are served from the builds as `dotnet run` serves them.** `PageServer`
reads each project's `staticwebassets.runtime.json`, so the page's own scripts are
the files in `wwwroot/` and the framework is the build's, with no publish. The viewer
is at `/viewer/` and the editor at `/editor/`, as the preset site lays them out.

**A scenario drives the page as a person or a script would**: `window.flyback`, a
finger, a mouse. What it asserts is what the page says it is doing, or what the browser
reports (pixels read off the canvas, a media player made).

## Consequences

- Seven scenarios take about thirty seconds, almost all of it the runtime loading
  interpreted.
- The image grows by the shell, about 120 MB unpacked.
- The pinned version is moved by hand: Dependabot does not see it.
- `window.flyback` is now held by tests, so a change to what it answers breaks a
  scenario rather than a script nobody runs.
