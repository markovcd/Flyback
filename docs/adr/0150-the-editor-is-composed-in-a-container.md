# ADR-0150: The editor and the viewer are each composed in a container

**Status:** Accepted · 2026-09-24 · *user-directed* · amends
[0148](0148-the-window-is-its-hubs-and-the-regions-around-them.md) and
[0017](0017-draw-the-node-editor-in-one-control.md)

## Context

[0148](0148-the-window-is-its-hubs-and-the-regions-around-them.md) turned the
window into hubs and regions, and [0017](0017-draw-the-node-editor-in-one-control.md)
did the same for the canvas. Both were wired by hand: the window's constructor
built some thirty objects in an order it had to get right, and handed each region
its dependencies and a lambda or two calling back into itself. A region that
needed another one it was built before took a `Func` of a field that was not set
yet. A test that wanted one part of it different had to add a property to the
window for the purpose (`SiteHttp`), or build the canvas and set a hook on it
(`KnobAnchor`).

The engineering guide asks for no dependency where a page of code will do. The
page of code was the window's constructor, and it had stopped being a page.

## Decision

The editor's window and its canvas are composed in a
`Microsoft.Extensions.DependencyInjection` container, one per window.
`EditorServices.AddEditor(setup)` registers the window, its hubs and regions and
what they read; `CanvasServices.AddCanvas()` registers the canvas and its services.
`EditorServices.Window(setup, replace)` builds one, and is what the program and the
tests both call.

- **Everything of the editor's own is built from its constructor.** A region takes
  the services it uses; one that needs a value of the setup takes the
  `EditorSetup`. A test that builds one by hand hands it the same: a
  `PluginCatalog.Empty` rather than a module catalog, an `EditorSetup` rather
  than a folder. Everything is a singleton, since the container is one window's.
- **A cycle is a `Lazy<T>`.** The take and the playback, the files and the
  playback, the plugins window and the unsaved question, the settings and the
  preset slot: one side takes the other lazily and asks for it only once it acts.
  `Lazy<>` is registered once, as an open generic.
- **A type registered twice under one meaning is keyed.** The site's
  `HttpClient` is registered under `SiteAccess.Client`, so a test hands over its
  own without the window knowing there is a test.
- **What the window called back into itself for became a service.** The unsaved
  question and saving (`UnsavedWork`), where the preset site is and what it is
  asked with (`SiteAccess`), offering a plugin a patch lacks (`PluginInstalls`),
  writing to the author (`StatusBar`), picking the startup patch (`PresetSlot`),
  what the output settings were last saved as (`OutputSections`) and putting a
  patch that has arrived on the canvas (`Playback.Show`).
- **A test swaps a service by registering it again.** `UiTest.NewMainWindow` and
  `UiTest.NewCanvas` take a `replace` callback; the last registration wins.
- **The viewer is composed the same way, one container per run.**
  `ViewerServices.AddViewer(launch)` registers the player, its window and what
  they play through; `ViewerServices.Window` and `ViewerServices.Player` build a
  run with a window or without one. The clock `--for` counts against is a
  `Func<TimeSpan>` a test registers again.

## Consequences

The window's constructor takes what it lays out and wires the events between
them; it no longer decides how anything is built or in what order. A region's
constructor says what it depends on, and `ValidateOnBuild` fails a window whose
container cannot build something, which `EditorServicesTests` and `ViewerServicesTests` check
for every registration.

Exceptions, each kept on purpose:

- **The window attaches itself to the `Shell`.** The regions are built before the
  window, since the window is built from them, so the one thing they cannot be
  handed is the window. `Shell.Attach` is the first line of its constructor, and
  a region reads `Shell.Owner` only once it is asked to do something.
- **Factories for what is not the editor's own.** The compiler, the sound
  device, the sound engine, MIDI and the assistant's column are shared with tests
  that build them by hand, so their constructors stay as they are and `AddEditor`
  and `AddViewer` build them in a factory. So does a value that may be absent
  (the saved presets, the recovery keeper): the container holds a null, and a
  class that reads one takes it as an optional parameter.
- **The container is never disposed.** The window already tears down what it
  holds in `OnClosed`, in the order a take and a device need; disposing the
  container as well would dispose them twice.
- **The plugins are still read before any window exists.** `Startup.Load` runs
  before Avalonia starts, and the container takes `Startup.Plugins` from there.
- **The viewer's device and MIDI backend are opened before its container.**
  `Program` opens them to say on the terminal what failed before any window
  exists, and hands them over on the `ViewerLaunch`.

No view models arrive with the container, and [0016](0016-build-the-ui-in-c-sharp-without-xaml.md)
stands as written.
