# ADR-0160: A patch plays in a browser on the engine compiled to WebAssembly

**Status:** Accepted · 2026-09-29 · *user-directed* · implemented in `src/Flyback.Web/`
and `src/Flyback.Gpu/IGl.cs`

## Context

Seeing and hearing a patch meant installing Flyback. VCV Rack's Cardinal runs in a
page as C++ compiled ahead of time to WebAssembly, which raised the question of
whether a stripped-down Flyback could do the same.

Three things stood in the way. The sound gets its desktop speed from IL generated
at run time ([0076](0076-the-processor-runs-a-program-as-il-once-it-is-built.md)),
which a browser does not allow. Plugins are assemblies loaded from a folder
([0025](0025-platform-io-behind-loadable-plugins.md)), which a page cannot read. And
the GPU renderer called OpenGL through native function pointers
([0157](0157-flyback-cli-render-draws-on-the-gpu.md)).

Measured before anything was built, the interpreter compiled ahead of time to
WebAssembly renders about 23 ns an op a sample, five times the desktop interpreter.
That is roughly 900 sound ops in real time. 32 of the 36 shipped presets fit; Whole
band (2,788 ops), Acid, Mycelium and Warehouse do not, or barely.

## Decision

**`Flyback.Web` is the engine, the compiler and the module plugins built for
`browser-wasm`, behind a page.** The page opens a shipped preset or a patch file,
plays it, and does nothing else: no editing and no panel, as with `flyback-viewer`
([0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md)). The seven
plugins that make modules are project references, loaded with
`PluginHost.LoadTypes`; none that talks to a device, a keychain or a model comes.

**The sound runs on the interpreter, ahead-of-time compiled.** The page renders
it a buffer at a time on its own thread and posts the buffers to an
`AudioWorklet`, which keeps a quarter of a second queued. The picture follows the
samples the worklet has played, not the ones rendered. The samples are the
desktop's to within one step of 16 bits.

**A patch whose sound cannot keep up plays its picture alone and says so.** On
opening, a fifth of a second of sound is timed on a copy. Below 1.2 times real
time the page draws on the wall clock and names the speed; a click on the speaker
plays the sound anyway.

**The picture is the desktop's own renderer on WebGL 2.** `IGl` is the interface
`GpuFrameRenderer` and `GpuReadback` call; `Gl` implements it natively and `WebGl`
through `gl.js`, which keeps each WebGL object under the integer the renderer
knows it by. The ES 3.0 dialect `GlslEmitter` already writes is WebGL 2's shading
language. A still read back through it matches `flyback-cli render --gpu` within
one level of 255.

**AOT is a publish switch.** `-p:RunAOTCompilation=true` needs the wasm-tools
workload and minutes; the gate builds the project interpreted on a stock SDK.
`hear.mjs` runs the build under Node with no page, which is what the specs use and
what an agent checks sound with.

## Consequences

- Heavy showcase patches play silently in a browser until the sound path gets
  faster there. A backend that emits JavaScript from `Op[]`, as `GlslEmitter` emits
  GLSL, is the lead; this ADR does not build it.
- The page's thread renders the sound, so a slow frame can starve the queue. A
  quarter of a second covers what was measured.
- The web build carries no lock file: its only packages are the SDK's own and move
  with it.
- The site does not serve it yet.
