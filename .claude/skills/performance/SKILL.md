---
name: performance
description: Use when a Flyback patch chokes, an export or a take is slow, or before proposing any work to make the engine faster (sound, picture, compile, IL, export) - the open leads, what has been measured and ruled out, and how to measure without fooling yourself.
---

# Performance

## Where the time goes

Tranquility (210 modules; a 3,314-op sound, a 1,433-op picture) on this machine (20 threads, RTX 4070 Super), IL throughout:

| What | Cost |
|---|---|
| Sound, interpreted | 15.6 µs a sample (48 kHz allows 20.8) |
| Sound, IL | 2.1 µs a sample |
| IL build after a shape-changing edit | 11-14 ms: 3 ms emit, the rest the JIT across half the cores |
| Patch compile, warm | 4-6 ms sound, 2-3 ms picture |
| Export, per 1080p frame | sound 16 ms, picture 42-45 ms (all cores), JPEG 13 ms |
| `flyback-cli render`, 5 s at 1080p to AVI | 17 s before the JPEG went parallel |

## Open leads

Measure first; do the work only if the number says so.

### 1. Export runs its three stages one after another

`MovieRenderer` renders a frame's sound, then its picture, then hands it to the encoder, all on one thread, so a frame costs their sum (about 71 ms above). The JPEG's 13 ms is its Huffman pass, which is sequential by nature, and the sound is one core.

- **Likely fix:** encode frame N on a thread of its own while frame N+1 draws (two pixel buffers), which takes the JPEG, or an ffmpeg pipe write, off the critical path; about 18% on Tranquility and most of the frame on a patch with a cheap picture. Running the sound ahead as well is worth up to another 20%, but `Meters.Refresh` and `Traces.Refresh` read the speaker's rings for the frame being drawn, so they would need a snapshot per frame.
- **Check:** the file has to come out byte for byte the same.

### 2. The picture on the GPU

Not measured. `GlslEmitter` puts every op in `main()`, so the shader runs a patch's frame-only ops (sequencers, envelopes, anything reading only the clock and knobs) once per pixel. The CPU path hoists them already: `FramePlan` sorts ops into frame, row and pixel stages (ADR-0064), and in Whole band 526 of 598 picture ops are frame-only.

- **Measure:** GPU frame time on a heavy picture at 1080p, on an integrated GPU: this machine's RTX 4070 Super is nowhere near a 600-op shader's ceiling. The viewer does not print its frame rate, so this needs a timer of its own. Worth doing only if a real patch drops frames.
- **Likely fix:** run the frame stage on the CPU once per frame and upload its registers as uniforms. The CPU is the reference (ADR-0035), so this moves the picture towards the specification rather than away from it.

## Ruled out, with numbers

Measured on Tranquility's sound, once IL chunking landed (ADR-0076, amendment of 2026-09-23):

- **A per-buffer stage for the sound** (ops reading only constants and live values, run once per buffer): 232 of 3,108 non-constant ops, 7.5%. Not worth the code.
- **Control rate for slow signals, and threads:** the sound runs at 2.27x real time on one core, roughly 7,000 ops before it chokes. Control rate breaks the bit-for-bit match with the interpreter and needs an ADR; threads are a large job. Reopen either only for a patch past that ceiling.
- **Chunk size:** 128, 256 and 512 ops per method measured the same; 1,024 fell to 1.13x. Keep 256.
- **What is left of the gap after an edit:** emit 3 ms, compile 4-6 ms, the check against the interpreter 1 ms; none is worth more code.
- **The CPU picture's frame stage, redone per row:** `SynthRenderer` reruns it on every row because each worker has its own bank. 526 ops over 1,080 rows against 72 a pixel over two million pixels is under half a percent.
- **A faster DCT or bit writer in `JpegWriter`:** micro. The frame is already spread across cores, and what stays sequential is the Huffman pass, which a faster DCT does not touch.

## Measuring without fooling yourself

- **Say which backend a number came from.** `flyback-cli render` runs IL unless given `--interpreted`; a build from before a0445d9 is always interpreted. ADR-0076's tables were read as "IL gains less on big patches" for a week when the JIT had in fact given up on them.
- **Time both arms in one process**, interpreted and IL, old and new, over the same patch. Across processes the machine's clocks move the numbers by more than most changes do. For a rewrite of a class, `git show HEAD:<file>` into a scratch copy under another name and run both side by side; the same harness checks the bytes.
- **Split before you guess.** Stopwatches around each stage of one frame found the JPEG costing as much as the picture, which the encoder's own comment said it never would.
- **Ask the JIT what it did:** `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1` works on a Release runtime and lists every method with its tier. `switched MinOpts` next to `CompiledPatch:Whole` means the method was too large to optimize.
- **Real patches need plugins.** `Flyback.Core.Benchmarks` references no plugins, so a user's patch will not open there. The quickest harness is a throwaway test in `Flyback.Plugins.Tests`, which loads the shipped plugins and carries the preset site's defaults (Tranquility among them): see `PresetSiteDefaultsTests.Open`. Run it with the test exe's `-method` and `-showliveoutput`. `src/Flyback.Cli/bin` has no plugins folder, so time the CLI from `dist/<rid>` after `release.sh`. Internal types (`IlEmitter`, `OpShape`) are reached by reflection.
- **Check the bits.** Anything that claims to be a faster route to the same program has to render the same samples. Compare a WAV byte for byte, and let `IlProgramTests` run over every preset.
