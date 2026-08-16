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
var output = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.VideoOutputTypeId);
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

- **No sink.** Compilation returns `CompiledPatch.Black` with no program to run,
  rather than failing. Deleting the Output node leaves a working editor showing
  black. An issue is reported only when the *other* sink is missing too: a patch
  with a Video Output and no Audio Output is silent on purpose, one with an Audio
  Output and no Video Output draws nothing on purpose, and neither wants telling
  about the sink it deliberately does not have on every edit. With both gone
  there is nothing to say it about but the patch, which does nothing at all —
  said once, from the video root, rather than twice from both.
- **A cycle.** `visiting` catches it, the offending module resolves to
  constant zero, and the status bar explains that feedback needs the
  `Feedback` module ([0012](0012-feedback-as-a-module-not-a-cycle.md)). The
  editor keeps running.

`CompileResult` carries `IReadOnlyList<CompileIssue>` alongside the program, each
optionally tagged with a node ID and each carrying an `IssueSeverity`. The status
bar shows every message whatever its severity; the node ID is currently unused,
and exists so the editor could later highlight the offending module.

A third thing is reported, and it is not a failure at all. An input marked
`PortSpec.Domain` — an oscillator's phase source, a sequencer's position — left
on its knob is a constant, and a module read across a constant does not move:
the oscillator holds one value instead of oscillating
([0030](0030-oscillators-accumulate-their-phase.md)), the sequencer holds step
one. That compiles to something entirely valid, which is the whole problem with
it. A patch built this way is silent, or a flat field, and nothing anywhere says
why. It is marked at the port because only the module knows which of its inputs
is its domain, and it is a `Warning` because a still is a legitimate thing to
want.

The failure modes above are not all the same weight, and callers that gate on
them need to tell them apart. A cycle and an unknown module are `Error` — what
compiled is a stand-in for something the patch got wrong. Having no sink at all
is `Warning`: a patch that reaches nobody is one somebody is part-way through
building, not one that is wrong.
`Error` is first in the enum, so it is the default of both the enum and
`CompileIssue`'s parameter — a complaint added later without anyone weighing it
blocks rather than slips through. `HasErrors` is the gate; `HasIssues` is still
what the status bar asks.

If multiple Output nodes exist, the first in list order wins. That is arbitrary
but harmless, and the alternative — rejecting the patch — would be worse.

Rooting at a sink rather than at *the* output turned out to be the load-bearing
choice. Adding sound meant parameterising which sink to start from and how wide
the result is; everything else — ordering, sharing, dead-code elimination, the
failure modes above — generalised without change
([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)). Neither sink
reports an issue for being absent while the other is present, because a patch
aimed at one of them is the normal case rather than a mistake; only a patch
aimed at neither is remarked on.
