# ADR-0022: Audio and video are two sinks over one patch

**Status:** Accepted · 2026-08-12

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

Option 3. Compilation is parameterised by the sink it starts from:

```csharp
public static CompileResult Compile(
    Patch patch,
    string sinkTypeId = NodeCatalog.OutputTypeId,
    int width = 3)
```

A second sink module, `audio.output`, takes `left` and `right` and is compiled
with `width: 2`. `CompiledPatch` carries `OutputWidth` alongside `OutputBase`.

## Consequences

The change was two lines of behaviour. `PatchCompiler` was already rooted at a
sink and walked backwards ([0011](0011-compile-backwards-from-output.md)) — only
*finding* the sink and *coercing* the result were video-specific. The defaulted
parameter meant all eight existing call sites kept working untouched, which is
what made "the existing 125 tests still pass" a real check rather than a
formality.

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
