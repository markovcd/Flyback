# ADR-0017: Draw the node editor in one custom control

**Status:** Accepted · 2026-08-11 · amended 2026-09-09, where the one control becomes one class across a file per region

## Context

The node editor is the largest interactive surface in the app. It must draw
modules with headers, port sockets, labels and knob values; draw bezier wires
between sockets; draw a grid; and support pan, zoom, selection, node dragging and
wire dragging.

The conventional approach is composition: a `Canvas` (or `ItemsControl` with a
`Canvas` panel) holding a templated control per node, with `Canvas.Left`/`Top`
bound to node position, plus a `Path` per wire. Nodes become real controls with
real hit-testing and real event handlers.

That model fights zooming. A `RenderTransform` on the canvas scales everything
including text rendering and hit-test geometry, and pointer coordinates need
manual inversion anyway. It also means node layout — where port 3's socket sits —
is expressed in a template but needed as *geometry* for wire endpoints, so the
same measurements exist twice, in two languages.

## Decision

One `Control` subclass that overrides `Render(DrawingContext)` and the pointer
handlers. `NodeGeometry` is the single source of truth for measurement:

```csharp
public static Rect Bounds(NodeInstance node, NodeDef def);
public static Point OutputPort(NodeInstance node, int index);
public static Point InputPort(NodeInstance node, NodeDef def, int index);
```

Painting and hit-testing both call these. Pan and zoom are one matrix pushed
around the whole render, with `ToGraph` inverting screen coordinates for input.

## Consequences

Socket positions cannot drift out of sync with where wires attach, because
`DrawConnections` and `HitPort` call the same `OutputPort`/`InputPort` methods
that `DrawNode` used to place the dots.

Zoom is `zoom * Math.Pow(1.12, e.Delta.Y)` plus a pan correction that pins the
point under the cursor. Text scales because it is drawn inside the transform.
There are no per-node controls to invalidate, so a zoom is one `InvalidateVisual`.

The whole editor was 477 lines when this was written, including painting,
hit-testing, five interaction modes and keyboard handling — smaller than the
templates, converters and attached-property plumbing the composed version would
need.

It is now about 2 700 across seven files. Groups, boxes, copy and paste, the
history, framing and a module list opened by gesture all arrived after this was
written, and each of them touches painting and hit-testing because that is what
this record decided they should do. The decision holds: those seven files are one
`partial class` and one `Render`, socket positions still come from
`NodeGeometry` alone, and nothing here became a control. What changed is which
file each part of it is written in, on the reasoning
[0039](0039-one-window-class-across-a-file-per-region.md) set out for the shell —
the region banners this file had carried for most of its life were the right
seams, and a banner is not a boundary. The files are named for the regions they
were: editing and the history, the clipboard, painting, groups, interaction, and
hit-testing.

The line count is worth keeping honest rather than quietly dropping, because it
was the evidence for the decision and it no longer reads as evidence for
anything. What it measures now is how much the editor does, not what drawing it
in one control costs; a bit over half of it is prose.

It made re-patching cheap to implement well: dragging a connected input picks the
existing wire up by its far end, which is one branch in `StartWire` because wires
are not objects with their own event handlers.

The costs are the ones custom drawing always has. No accessibility — the editor
is invisible to screen readers, where composed controls would have been
announced. No tooltips or context menus on nodes without hand-rolling them; the
palette buttons have tooltips, the nodes do not. Hit-testing is linear over nodes
in reverse z-order, fine for hundreds and not for tens of thousands. And text is
laid out per node per frame via `FormattedText`, with no caching — measurable at
scale, invisible at the scale this runs.

Rendering is also unclipped per node: a long port name is trimmed with an
ellipsis via `MaxTextWidth`, but nothing enforces that a module's contents stay
inside its own bounds. `NodeGeometry` sizing keeps them there by construction.
