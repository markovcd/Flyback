# ADR-0022: Audio and video are two sinks over one patch

**Status:** Accepted · 2026-08-12 · amended by
[0027](0027-delay-lines-give-the-audio-path-a-memory.md), which introduces the
first modules that do their job in one sink's program and not the other's

## Context

The engine evaluates `(x, y, t) → value` through a flat register machine
([0005](0005-compile-to-a-flat-register-machine.md)). Audio is that same machine
in a degenerate case: only `t` varies, and the result is a scalar rather than a
colour. So the 52 modules that make pictures can make sound, and the question is
only what shape the seam takes.

Three ways to add sound:

1. **A separate audio graph.** Its own modules, its own editor, its own
   compiler. Honest about the differences, and duplicates almost everything.
2. **One graph, one program, both outputs.** Compile once and read RGB from one
   place and stereo from another. But then every audio module is evaluated per
   pixel and every video module per sample, which is the worst of both.
3. **One graph, one program per sink.**

## Decision

Option 3. Compilation is parameterised by the sink it starts from, with one
entry point per sink:

```csharp
public static CompileResult CompileForVideo(this Patch patch) =>
    Compile(patch, NodeCatalog.VideoOutputTypeId, NodeCatalog.VideoChannels);

public static CompileResult CompileForAudio(this Patch patch) =>
    Compile(patch, NodeCatalog.AudioOutputTypeId, NodeCatalog.AudioChannels);
```

A second sink module, `audio.output`, takes `left` and `right` and is compiled
two registers wide. `CompiledPatch` carries `OutputWidth` alongside `OutputBase`.
The shared `Compile(patch, sinkTypeId, width)` core is private: a sink is not an
open-ended axis, and callers that pick a sink type by hand are how the two
programs drift apart.

## Consequences

The change was two lines of behaviour. `PatchCompiler` was already rooted at a
sink and walked backwards ([0011](0011-compile-backwards-from-output.md)) — only
*finding* the sink and *coercing* the result were video-specific. As first
written, `Compile` took the sink and width as defaulted parameters, so all eight
existing call sites kept working untouched — which is what made "the existing 125
tests still pass" a real check rather than a formality. The named entry points
above replaced those defaults once the second sink was no longer new; see
Amendments.

Dead-code elimination across sinks falls out for free, because it was never a
pass in the first place. A noise module only the screen reaches emits no ops in
the audio program; an oscillator only the speakers reach emits none in the video
program. The Gherkin scenario asserting exactly that is the clearest statement of
what this ADR buys.

Cost is proportionate to what each sink actually reads. Video is ~31M
evaluations/sec at 960×540; stereo at 48 kHz with 4× oversampling
([0023](0023-oversample-the-audio-path.md)) is 192k/sec, and both channels come
out of a single evaluation because left and right are two slots in one program.
Measured, the audio path renders about 34× faster than realtime — roughly 3% of
one core.

Two things do not carry across, and both are honest consequences rather than
gaps. `SampleFeedback` reads the previous *frame*; there is no such thing on the
audio timeline, so it reads silence — an audio delay line would be a separate
module. And `Coordinates` yields whatever the caller supplies for x and y, which
for audio is either zero or a raster sweep; that choice belongs to the renderer,
not the program, since `LoadX` and `LoadY` are inputs rather than constants.

The normalled input this needed (`right` carrying `left` when unpatched) turned
out to generalise: `PortSpec.NormalledFrom` makes the hardware concept
[0009](0009-editable-defaults-on-every-input.md) already invoked by name into
something any module can declare.

## Amendments

**2026-08-12 — one entry point per sink.** The defaulted `sinkTypeId` and `width`
parameters were removed and `Compile` made private, replaced by the
`CompileForVideo` / `CompileForAudio` extension methods shown above. The defaults
did their job during the change and became a liability after it: `Compile(patch)`
silently meant *video*, and the two sinks read as one primary and one afterthought
rather than as the pair this ADR argues for. The widths moved to
`NodeCatalog.VideoChannels` and `NodeCatalog.AudioChannels`.

**2026-08-12 — `output` renamed to `video.output`.** The video sink's `TypeId` was
the unqualified `"output"`, predating audio; it is now `video.output`, matching
`audio.output`. This is the breaking `TypeId` rename
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) warns about, done while
no saved patches existed anywhere.
