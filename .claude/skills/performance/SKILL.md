---
name: performance
description: Use when a Flyback patch chokes, or before proposing any work to make the engine faster (sound, picture, compile, IL) - the open leads nobody has measured yet, what has been measured and ruled out, and how to measure without fooling yourself.
---

# Performance

## Open leads

Neither of these is measured. Measure first; do the work only if the number says so.

### 1. The gap after an edit (the likelier one)

An edit that changes a program's shape plays interpreted until `IlCompiler` has built its IL. For Tranquility (a 210-module, 3,319-op sound) that build takes about 56 ms, and the interpreter runs that patch at 0.66-0.75x real time, so every rewire or added module probably drops out for a moment. That is choking while patching, which is when a person notices.

- **Measure:** play a heavy patch through `AudioRenderer` with an `IlCompiler` attached, submit a shape-changing recompile, and count buffers that took longer than their own duration until the IL lands. Knob moves do not count: they rebind code of the same shape at once.
- **Likely fix:** the chunks `IlEmitter.EmitChunks` writes are independent `DynamicMethod`s, so they can be emitted and put through the JIT on several threads at once. `IlMethods.Prepare` calls each method once to force the JIT; that is the step to spread. It keeps the bits and should cut the gap by about the core count.

### 2. The picture on the GPU

`GlslEmitter` puts every op in `main()`, so the shader runs a patch's frame-only ops (sequencers, envelopes, anything reading only the clock and knobs) once per pixel. The CPU path hoists them already: `FramePlan` sorts ops into frame, row and pixel stages (ADR-0064), and in Whole band 526 of 598 picture ops are frame-only.

- **Measure:** GPU frame time on a heavy picture at 1080p. The viewer does not print its frame rate, so this needs a timer of its own. Worth doing only if a real patch drops frames.
- **Likely fix:** run the frame stage on the CPU once per frame and upload its registers as uniforms. The CPU is the reference (ADR-0035), so this moves the picture towards the specification rather than away from it.

## Ruled out, with numbers

Measured on Tranquility's sound, once IL chunking landed (ADR-0076, amendment of 2026-09-23):

- **A per-buffer stage for the sound** (ops reading only constants and live values, run once per buffer): 232 of 3,108 non-constant ops, 7.5%. Not worth the code.
- **Control rate for slow signals, and threads:** the sound runs at 2.27x real time on one core, roughly 7,000 ops before it chokes. Control rate breaks the bit-for-bit match with the interpreter and needs an ADR; threads are a large job. Reopen either only for a patch past that ceiling.
- **Chunk size:** 128, 256 and 512 ops per method measured the same; 1,024 fell to 1.13x. Keep 256.

## Measuring without fooling yourself

- **Say which backend a number came from.** `flyback-cli render` runs IL unless given `--interpreted`; a build from before a0445d9 is always interpreted. ADR-0076's tables were read as "IL gains less on big patches" for a week when the JIT had in fact given up on them.
- **Time both arms in one process**, interpreted and IL, over the same patch. Across processes the machine's clocks move the numbers by more than most changes do.
- **Ask the JIT what it did:** `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1` works on a Release runtime and lists every method with its tier. `switched MinOpts` next to `CompiledPatch:Whole` means the method was too large to optimize.
- **Real patches need plugins.** `Flyback.Core.Benchmarks` references no plugins, so a user's patch will not open there. A scratch console app referencing `Flyback.Core`, `Flyback.Engine` and `Flyback.Plugins` that calls `NodeCatalog.Install(PluginHost.Load(<App's bin>/plugins).Modules)` and `PatchFile.Open` does it; build `src/Flyback.App` in Release first so the plugins folder exists. `OpShape` is internal, so reach it by reflection.
- **Check the bits.** Anything that claims to be a faster route to the same program has to render the same samples. Compare a WAV byte for byte, and let `IlProgramTests` run over every preset.
