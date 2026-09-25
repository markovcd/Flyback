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
  the services it uses, and no object bundles the ones every region shares; one that needs a value of the setup takes the
  `EditorSetup`. A test that builds one by hand hands it the same: a
  `PluginCatalog.Empty` rather than a module catalog, an `EditorSetup` rather
  than a folder. Everything is a singleton, since the container is one window's.
- **A class below Flyback.App declares the setup it reads as an interface.**
  `IlCompiler` takes an `IIlCompilerSetup`, which `EditorSetup` and the viewer's
  `ViewerOptions` both implement, and a test that builds one by hand passes none.
- **A cycle is a `Lazy<T>`.** The take and the playback, the files and the
  playback, the plugins window and the unsaved question, the settings and the
  preset slot: one side takes the other lazily and asks for it only once it acts.
  `Lazy<>` is registered once, as an open generic.
- **The site's `HttpClient` is a named client of `AddHttpClient`.** `SiteAccess`
  asks the factory for `SiteAccess.Client`, and a test gives that client a handler
  of its own without the window knowing there is a test.
- **What the window called back into itself for became a service.** The unsaved
  question and saving (`UnsavedWork`), where the preset site is and what it is
  asked with (`SiteAccess`), offering a plugin a patch lacks (`PluginInstalls`),
  writing to the author (`StatusBar`), picking the startup patch (`PresetSlot`),
  what the output settings were last saved as (`OutputSections`), putting a
  patch that has arrived on the canvas (`Playback.Show`) and what the assistant's
  column reads and edits (`IAssistantEditor`).
- **A test swaps a service by registering it again.** `UiTest.NewMainWindow` and
  `UiTest.NewCanvas` take a `replace` callback; the last registration wins.
- **The viewer is composed the same way, one container per run.**
  `ViewerServices.AddViewer(launch)` registers the player, its window and what
  they play through; `ViewerServices.Window` and `ViewerServices.Player` build a
  run with a window or without one. The container is disposed when the window
  closes, or when the windowless `PlayerRun` is, and disposes the player before
  the engine, the compiler and MIDI it plays through. The clock `--for` counts
  against is a `WallClock` over a `TimeProvider`, which a test registers again.

## Consequences

The window's constructor takes what it lays out and wires the events between
them; it no longer decides how anything is built or in what order. A region's
constructor says what it depends on, and `ValidateOnBuild` fails a window whose
container cannot build something, which `EditorServicesTests` and `ViewerServicesTests` check
for every registration.

Exceptions, each kept on purpose:

- **What a region asks of the window is a service over `EditorWindow`.**
  The regions are built before the window, since the window is built from them.
  Dialogs, the file pickers, the monitors, whether the window is in front and
  closing it are each an interface (`IDialogs`, `IFilePickers`, `IMonitors`,
  `IWindowFocus`, `IWindowClose`) whose implementation asks `EditorWindow` for the
  window only once it is used. `MainWindow` is registered through
  `EditorWindow.Build`, and asking for the window while it is being built throws,
  where the container would otherwise build a second window to answer.
- **Factories for what is only looked up.** Opening the sound device is a call
  `AddEditor` makes in a factory, and the sound engine is given the `AudioSetup` it
  returns; `Playback` hands it any later device. Nothing is registered as
  null. A service that may have nothing to work with does nothing instead: the
  recovery keeper and the saved presets with no folder (`PresetLibrary.Keeps`
  says which), and `NoMidiInput`, with no ports, which `PluginCatalog.PreferredMidiInput`
  hands over where no plugin can hear MIDI.
- **The window's container is disposed when the window closes.** `OnClosed`
  finishes the take and stops the recovery keeper first; the container then
  disposes the sound engine, the compiler and MIDI. The engine owns its device and
  is handed it in an `AudioSetup`, so the container never owns a device the engine
  may already have swapped and disposed.
- **The plugins are still read before any window exists.** `Startup.Load` runs
  before Avalonia starts, and the window is handed what it loaded on
  `EditorSetup.Plugins`, which a test leaves at `PluginCatalog.Empty`.
- **The viewer's device and MIDI backend are opened before its container.**
  `Program` opens them to say on the terminal what failed before any window
  exists, and hands them over on the `ViewerLaunch`.

No view models arrive with the container, and [0016](0016-build-the-ui-in-c-sharp-without-xaml.md)
stands as written.
