# ADR-0011: Compile backwards from the Output node

**Status:** Accepted · 2026-08-11

## Context

A patch under construction is rarely a clean tree. Modules get dropped on the
canvas before being wired. Branches get unplugged and left in place while
something else is tried. Half-built ideas sit off to one side.

If compilation walks the node *list*, all of that is compiled and evaluated —
per pixel, sixty times a second — despite contributing nothing to the image. A
detached branch of expensive noise modules would cost exactly as much as a
connected one.

## Decision

Start at the `Output` node and resolve dependencies recursively, memoising by
node ID:

```csharp
var output = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.OutputTypeId);
var result = Resolve(output);
```

`Resolve` walks each input's incoming wire, recurses, then calls the module's
`EmitFn`. A `resolved` dictionary makes shared upstream modules emit once; a
`visiting` set detects cycles.

## Consequences

Dead code elimination is not a pass — it is a consequence of the traversal
order. Anything Output cannot reach is never visited, so it emits no ops and
costs nothing. Leaving experiments lying around on the canvas is free, which
matters for a tool meant to be played with.

Common subexpressions are shared for the same reason. `Coordinates` feeding four
modules is resolved once and its slots reused, so `LoadX` is emitted once no
matter how many consumers it has.

Evaluation order falls out correctly without a separate topological sort: a
node's `EmitFn` runs only after all its inputs have been resolved, so ops are
appended in dependency order by construction.

Two failure modes are handled rather than thrown:

- **No Output node.** Compilation returns `CompiledPatch.Black` with an issue
  reported to the status bar, rather than failing. Deleting the Output node
  leaves a working editor showing black.
- **A cycle.** `visiting` catches it, the offending module resolves to
  constant zero, and the status bar explains that feedback needs the
  `Feedback` module ([0012](0012-feedback-as-a-module-not-a-cycle.md)). The
  editor keeps running.

`CompileResult` carries `IReadOnlyList<CompileIssue>` alongside the program, each
optionally tagged with a node ID. The status bar shows the messages; the node ID
is currently unused, and exists so the editor could later highlight the offending
module.

If multiple Output nodes exist, the first in list order wins. That is arbitrary
but harmless, and the alternative — rejecting the patch — would be worse.

Rooting at a sink rather than at *the* output turned out to be the load-bearing
choice. Adding sound meant parameterising which sink to start from and how wide
the result is; everything else — ordering, sharing, dead-code elimination, the
failure modes above — generalised without change
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)). Only the missing
video sink still reports an issue, because a patch with no audio is the normal
case rather than a mistake.
