---
name: performance
description: Use when a Flyback patch chokes, or before proposing any work to make the engine faster (sound, picture, compile, IL) - the open leads nobody has measured yet, what has been measured and ruled out, and how to measure without fooling yourself.
---

# Performance

## Open lead: the picture on the GPU

Not measured. Measure first; do the work only if the number says so.

`GlslEmitter` puts every op in `main()`, so the shader runs a patch's frame-only ops (sequencers, envelopes, anything reading only the clock and knobs) once per pixel. The CPU path hoists them already: `FramePlan` sorts ops into frame, row and pixel stages (ADR-0064), and in Whole band 526 of 598 picture ops are frame-only.

- **Measure:** GPU frame time on a heavy picture at 1080p, on an integrated GPU: this machine's RTX 4070 Super is nowhere near a 600-op shader's ceiling. The viewer does not print its frame rate, so this needs a timer of its own. Worth doing only if a real patch drops frames.
- **Likely fix:** run the frame stage on the CPU once per frame and upload its registers as uniforms. The CPU is the reference (ADR-0035), so this moves the picture towards the specification rather than away from it.

## Ruled out, with numbers

Measured on Tranquility's sound, once IL chunking landed (ADR-0076, amendment of 2026-09-23):

- **A per-buffer stage for the sound** (ops reading only constants and live values, run once per buffer): 232 of 3,108 non-constant ops, 7.5%. Not worth the code.
- **Control rate for slow signals, and threads:** the sound runs at 2.27x real time on one core, roughly 7,000 ops before it chokes. Control rate breaks the bit-for-bit match with the interpreter and needs an ADR; threads are a large job. Reopen either only for a patch past that ceiling.
- **Chunk size:** 128, 256 and 512 ops per method measured the same; 1,024 fell to 1.13x. Keep 256.
- **What is left of the gap after an edit:** the chunks already go through the JIT side by side, and the sound builds in 11-14 ms. Emitting the IL is 3 ms of that, the patch compile 4-6 ms and the check against the interpreter 1 ms; none is worth more code.

## Measuring without fooling yourself

- **Say which backend a number came from.** `flyback-cli render` runs IL unless given `--interpreted`; a build from before a0445d9 is always interpreted. ADR-0076's tables were read as "IL gains less on big patches" for a week when the JIT had in fact given up on them.
- **Time both arms in one process**, interpreted and IL, over the same patch. Across processes the machine's clocks move the numbers by more than most changes do.
- **Ask the JIT what it did:** `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1` works on a Release runtime and lists every method with its tier. `switched MinOpts` next to `CompiledPatch:Whole` means the method was too large to optimize.
- **Real patches need plugins.** `Flyback.Core.Benchmarks` references no plugins, so a user's patch will not open there. The quickest harness is a throwaway test in `Flyback.Plugins.Tests`, which loads the shipped plugins and carries the preset site's defaults (Tranquility among them): see `PresetSiteDefaultsTests.Open`. Otherwise a scratch console app referencing `Flyback.Core`, `Flyback.Engine` and `Flyback.Plugins` that calls `NodeCatalog.Install(PluginHost.Load(<App's bin>/plugins).Modules)` and `PatchFile.Open` does it; build `src/Flyback.App` in Release first so the plugins folder exists. `OpShape` is internal, so reach it by reflection.
- **Check the bits.** Anything that claims to be a faster route to the same program has to render the same samples. Compare a WAV byte for byte, and let `IlProgramTests` run over every preset.
