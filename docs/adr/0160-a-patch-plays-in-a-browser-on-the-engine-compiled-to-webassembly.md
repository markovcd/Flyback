# ADR-0160: A patch plays in a browser on the engine compiled to WebAssembly

**Status:** Accepted · 2026-09-29 · *user-directed* · implemented in `src/Flyback.Web/`,
`src/Flyback.Gpu/IGl.cs` and `src/Flyback.Engine/Compile/JsEmitter.cs`

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

The interpreter compiled ahead of time to WebAssembly renders about 23 ns an op a
sample, five times the desktop interpreter: roughly 900 sound ops in real time.
Every teaching preset fits. Whole band (2,788 ops), Acid, Mycelium, Warehouse, No
Sense Dub and Slow weather do not.

## Decision

**`Flyback.Web` is the engine, the compiler and the module plugins built for
`browser-wasm`, behind a page.** The page opens a shipped preset or a patch file,
plays it, and does nothing else: no editing and no panel, as with `flyback-viewer`
([0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md)). It offers no
presets of its own: the presets page sends it one, and it leads back there. The seven
plugins that make modules are project references, loaded with
`PluginHost.LoadTypes`; none that talks to a device, a keychain or a model comes.

**The sound runs as JavaScript that `JsEmitter` writes from the program.** It is
`CompiledPatch.Evaluate` transcribed, as `GlslEmitter` transcribes it for the
picture, cut into functions of 128 ops with registers in locals, as the IL is cut
into methods. The script works in place on the program's memory, pinned in the
runtime's heap (`JsLayout`, `JsSound`), and hands its evaluations to
`AudioRenderer.Decimate`, so the filter, the clock, every Meter and Scope, a rewind
and a seek are the interpreter's own. A power remembers its last operands and answers
from them while they hold, which is exact and a third of Whole band's time. Constants
are read from the layout rather than written into the script, and scripts are kept by
their text, so a knob turned runs the function the engine has already optimized. Where a
program cannot be emitted, which is one reading a picture, the interpreter plays it.

The script's samples are the interpreter's to the bit on this machine, preset for
preset (`JsProgramTests`); a browser's `Math.sin` or `exp` may round its last bit
otherwise, which the tests allow a hair for. Under Node, Whole band renders at 2.0
times real time and Warehouse, the heaviest at 4,346 ops, at 1.2: about 1 ns an op
an evaluation, a sixth of the interpreter's.

**The sound renders in a worker, in a second runtime, and feeds an `AudioWorklet`
straight**, which keeps a quarter of a second queued. The page's runtime compiles
and draws the picture alone. Nothing is shared: the worker posts the samples down
a channel to the worklet, and the Meters' readings and the computer keyboard's
voices to the page, and after them every Scope's and Analyzer's buffer, which is all the
picture knows of the sound; the shader reads a chart as a texture
([0167](0167-the-shader-reads-a-table-as-a-float-texture.md)). A panel knob is turned in both runtimes. A key is laid out
by the page and played by the worker, through the editor's own `ComputerKeyboard` and
`VoicePool`, and a patch played on it keeps a tenth of a second queued, not a quarter. The picture follows the samples the worklet has
played, not the ones rendered, and shows a turned knob once the sound has caught up.

**A patch whose sound cannot keep up plays its picture alone and says so.** While
the page waits to play, the worker renders three seconds nobody hears, so the
engine has optimized the script before the first play seeks back to the start. The
script is then judged by the dropouts it makes once three seconds have let it
settle: more than twenty in two seconds and the sound is worked out a step lower
([0168](0168-the-sound-is-oversampled-2x-and-a-setting.md)), and at 1× the picture takes the
wall clock. The web editor steps down the same way and never gives the sound up.
The interpreter is judged on opening, by a fifth of a second timed on a copy, below
1.2 times real time. Either way a click on the speaker plays the sound anyway.

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

- The heaviest presets have little to spare: in a browser, Whole band renders at
  about 1.6 times real time and Warehouse at 1.3 to 1.5, so on a slower machine
  they may fall back to the picture. The engine's speed is the limit now, not the
  page.
- The page loads the runtime twice, which costs memory and a second start. It needs
  no cross-origin isolation, so any static host serves it.
- A knob turned or a key struck is heard as late as the queue is long: a quarter of
  a second, or a tenth for a patch played on the computer keyboard, which stutters
  sooner on a heavy patch. A MIDI keyboard would come through Web MIDI into the
  same `VoicePool`, and is not wired yet.
- An opcode added to the interpreter needs a line in `JsEmitter`, or the web viewer
  plays every program using it on the interpreter; `JsProgramTests` fails for it first.
- The web build carries no lock file: its only packages are the SDK's own and move
  with it.
- The preset site serves it at `/viewer/`; the site's image installs the workload to compile it ahead of time, which adds minutes to its build. GitHub Pages carries it at `viewer/` too, compiled the same way in the SDK image by the Pages workflow rather than committed.
- Both serve one presets page, `site/presets.html`. It lists the shipped presets from the build's stills index ([0163](0163-a-build-draws-every-presets-still-once.md)), marked as built in and filtered in the page, and on the preset site the shared presets beside them from its API, which GitHub Pages does not have. A shipped preset plays in the viewer by `?preset=`, a shared one by `?file=`.
