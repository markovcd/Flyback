# ADR-0015: Avalonia for the UI shell

**Status:** Accepted · 2026-08-11

## Context

The shell has three demanding requirements and very few ordinary ones. It needs:

1. **A raw pixel buffer.** Frames are rendered to BGRA bytes on a background
   thread ([0018](0018-never-render-frames-on-the-ui-thread.md)) and must be
   blitted into something displayable. This is the whole program.
2. **A custom drawing surface.** The node editor paints itself
   ([0017](0017-draw-the-node-editor-in-one-control.md)) — nodes, bezier wires,
   grid, labels.
3. **Desktop pointer input.** Middle- and right-drag to pan, wheel deltas to
   zoom, per-button state during drags, keyboard focus.

It needs almost nothing that a UI framework is usually chosen for: no data
binding of consequence, no native platform look, no navigation, no forms.

## Decision

Avalonia 12.1.1, with `Avalonia.Desktop`, `Avalonia.Themes.Fluent` and
`Avalonia.Fonts.Inter`.

## Alternatives considered

**MAUI.** Mobile-first, built around composing native platform controls — almost
none of which this app uses. It has no writable bitmap with a lockable buffer, so
requirement 1 would mean taking a SkiaSharp dependency to reach the single most
important capability. Custom drawing is `GraphicsView`/`IDrawable` over
`Microsoft.Maui.Graphics`, a thinner separate abstraction. Its pointer story is
built around touch; which mouse button and wheel delta generally means dropping
into platform-specific handlers, i.e. writing WinUI code to work around the
cross-platform framework. It also requires `dotnet workload install maui` plus
the Windows App SDK, neither of which was present, and forces a
`net10.0-windows...` TFM ([0001](0001-target-net-10.md)).

**WPF.** Technically the closest fit — `WriteableBitmap` and `OnRender` are the
APIs Avalonia modelled itself on, and it would have worked. Rejected for being
Windows-only and effectively frozen; Avalonia gives the same programming model
with a live upstream and Linux support, which matters for a creative tool.

**WinForms.** `Graphics`/GDI+ would handle the blit, but the node editor would
mean hand-rolling transforms, and there is no modern styling.

**Silk.NET or OSVR-style raw windowing + OpenGL.** Minimal dependency, maximum
control, and the natural host if the GPU backend
([0003](0003-cpu-rendering-with-a-gpu-path-left-open.md)) ever lands. Rejected
because it provides no controls at all — the toolbar, palette, inspector,
sliders, combo boxes and file dialogs would all have to be built from nothing,
which is most of the shell.

## Consequences

`WriteableBitmap.Lock()` yields a raw `Address` and `RowBytes`, so the finished
frame is a `Buffer.MemoryCopy` away from the screen — no format conversion, no
intermediate image object.

The app is a plain `net10.0` WinExe with no workload requirement, and it built
and ran on the target machine unmodified. It also runs on Linux and macOS, which
neither WPF nor MAUI offers together.

Avalonia 12 is a recent major release. All the APIs used here — `DrawingContext`,
`FormattedText`, `StreamGeometry`, `WriteableBitmap`, `StorageProvider` — matched
the WPF-derived shapes expected of them, with no version-specific workarounds.
The one place it surprised was `GridSplitter`, which squeezes a fixed-pixel
column to nothing; the flexible columns are star-sized as a result.

The lock-in is contained. Core has no UI dependency
([0002](0002-split-engine-from-shell.md)), so everything Avalonia-specific is
the 1,229 lines in `VideoSynth.App`.
