---
name: performance
description: Use when a Flyback patch chokes, an export or a take is slow, before proposing any work to make the engine faster (sound, picture, compile, IL, export), or to tell whether a change made anything faster or slower - the open leads, what has been measured and ruled out, comparing two builds with scripts/bench-compare.sh, and how to measure without fooling yourself.
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
| Export, per 1080p frame | sound 16 ms, picture 42-45 ms on the processor (all cores), JPEG 13 ms |
| `flyback-cli render`, 5 s at 1080p to AVI | 17 s before the JPEG went parallel |
| Whole band, 5 s at 1080p to MP4 | 7.1 s with `--processor`, 4.0 s on the GPU, of which the sound is 1.9 s (ADR-0157) |

## Open leads

Measure first; do the work only if the number says so.

### 1. Export runs its three stages one after another

`MovieRenderer` renders a frame's sound, then its picture, then hands it to the encoder, all on one thread, so a frame costs their sum (about 71 ms above). With the picture on the GPU the sound is most of what is left, and the GPU sits idle while it runs: `HeadlessRenderer` reads each frame back with a stalling `glReadPixels`, where the two pixel buffers `GpuReadback` already keeps for a take would overlap the read with the next frame's sound. The JPEG's 13 ms is its Huffman pass, which is sequential by nature, and the sound is one core.

- **Likely fix:** encode frame N on a thread of its own while frame N+1 draws (two pixel buffers), which takes the JPEG, or an ffmpeg pipe write, off the critical path; about 18% on Tranquility and most of the frame on a patch with a cheap picture. Running the sound ahead as well is worth up to another 20%, but `Meters.Refresh` and `Traces.Refresh` read the speaker's rings for the frame being drawn, so they would need a snapshot per frame.
- **Check:** the file has to come out byte for byte the same.

### 2. The picture on the GPU

About 10 ms a 1080p frame for Whole band in `flyback-cli render`, readback, swizzle and encode included; the shader's own share not split out. `GlslEmitter` puts every op in `main()`, so the shader runs a patch's frame-only ops (sequencers, envelopes, anything reading only the clock and knobs) once per pixel. The CPU path hoists them already: `FramePlan` sorts ops into frame, row and pixel stages (ADR-0064), and in Whole band 526 of 598 picture ops are frame-only.

- **Measure:** GPU frame time on a heavy picture at 1080p, on an integrated GPU: this machine's RTX 4070 Super is nowhere near a 600-op shader's ceiling. The viewer does not print its frame rate, so this needs a timer of its own. Worth doing only if a real patch drops frames.
- **Likely fix:** run the frame stage on the CPU once per frame and upload its registers as uniforms. The CPU is the reference (ADR-0035), so this moves the picture towards the specification rather than away from it.

### 3. The web viewer's sound

`JsEmitter`'s script, AOT build, under Node 18 (ADR-0160), at 4x oversampling (the default 2x is twice these): Whole band 2.0x real time, Acid and Mycelium about 1.6x, Warehouse (4,346 ops) and No Sense Dub about 1.2x, Slow weather 1.1x; about 1 ns an op an evaluation. Chrome is faster than Node's older V8. The AOT interpreter it replaced was 23 ns an op a sample, a quarter of real time for Whole band. After remembering each power's last operands, no function takes more than 7%: `advance`, `readLine`, `noise3` and `hash` lead.

- **Done:** the sound renders in a worker of its own (`speaker.js`), with nothing shared; the page gets the Meters' readings by message. In Chrome, AOT, on a busy machine, Whole band held 1.6x with no dropouts and Warehouse 1.3 to 1.5x with some. A cold script runs Warehouse at 0.6x, so the worker warms it up for three seconds before the first play. What is left is the script's own speed: `advance`, `readLine`, `noise3` and `hash`.
- **Measure:** `node artifacts/web/hear.mjs --preset X --seconds 8` prints `speed`; `--cpu-prof` on the same line gives a profile. A 0.2 s timing on a freshly made script is before V8 has optimized it and reads low.

## Ruled out, with numbers

Measured on Tranquility's sound, once IL chunking landed (ADR-0076, amendment of 2026-09-23):

- **A per-buffer stage for the sound** (ops reading only constants and live values, run once per buffer): 232 of 3,108 non-constant ops, 7.5%. Not worth the code.
- **Control rate for slow signals, and threads:** the sound runs at 2.27x real time on one core, roughly 7,000 ops before it chokes. Control rate breaks the bit-for-bit match with the interpreter and needs an ADR; threads are a large job. Reopen either only for a patch past that ceiling.
- **Chunk size:** 128, 256 and 512 ops per method measured the same; 1,024 fell to 1.13x. Keep 256.
- **What is left of the gap after an edit:** emit 3 ms, compile 4-6 ms, the check against the interpreter 1 ms; none is worth more code.
- **The CPU picture's frame stage, redone per row:** `SynthRenderer` reruns it on every row because each worker has its own bank. 526 ops over 1,080 rows against 72 a pixel over two million pixels is under half a percent.
- **The web viewer's chunk size:** 256 ops a function left No Sense Dub and Slow weather's larger functions unoptimized (0.5x and 0.6x); 64 moved too many registers through the bank (Warehouse 0.72x). 128 is best for all.
- **Reusing a web script by its text:** a second function made from one text, by a cache of scripts or by V8's own for `new Function`, runs a third slower for good (Whole band at 2x: 3.9x fresh, 2.5x after a switch to 4x and back). Each script's text is unique, and an edit of constants alone retunes the playing script in place.
- **Baking the knobs in for the web viewer:** Warehouse is 4,337 ops baked against 4,346 played. Not what makes a patch heavy.
- **A faster DCT or bit writer in `JpegWriter`:** micro. The frame is already spread across cores, and what stays sequential is the Huffman pass, which a faster DCT does not touch.

## Comparing two builds

`./scripts/bench-compare.sh BASE [HEAD]` answers whether a change made anything faster or slower. It builds each side in a worktree of its own under `$TMPDIR/flyback-bench` (kept, so the same base builds once), runs `Flyback.Core.Benchmarks` on both, alternating round by round, and with `--web "Whole band,Acid"` plays those presets through each side's AOT web viewer under Node. A change is called only when it passes `--threshold` (5%) and a Mann-Whitney test says the two sets of timings differ; allocations per operation are compared exactly. HEAD `.` is the working tree, uncommitted edits included, so a change can be checked before it is committed. `--json` for a script, `--clean` to drop the kept worktrees, `--help` for the rest.

- **Filter it.** All 65 cases take about nine minutes a side a round; `--filter '*AudioBenchmarks*'` is a minute. A web build is ten minutes a side the first time.
- **Rounds are samples.** The benchmarks give BenchmarkDotNet's iterations for every round; the web viewer gives one number a run, so `--web-rounds` below 4 can never call a change.
- **In process only.** BenchmarkDotNet's default toolchain looks for the project across the whole clone, finds a copy in each of `.claude/worktrees`, and refuses to run. The script passes `--inProcess`; run the exe by hand the same way.
- **A number that moves between runs is the machine first.** A leftover `node` from a stopped run once made the old build read 1.27x where it measures 1.96x. The alternating rounds and the test are what keep that from reading as a change.
- **Timings never gate.** The gate takes what is exact: a method the JIT gave up on, an allocation on the sound's thread, a script made where one was retuned, an op count over its budget. A slowdown found here earns a test of its cause, not a threshold on its time.

## Measuring without fooling yourself

- **Say which backend a number came from.** `flyback-cli render` draws on the GPU unless given `--processor`, and on the processor it runs IL unless given `--interpreted`; a build from before a0445d9 is always interpreted. ADR-0076's tables were read as "IL gains less on big patches" for a week when the JIT had in fact given up on them.
- **Time both arms in one process** where they can share one, interpreted and IL, old and new, over the same patch. Across processes the machine's clocks move the numbers by more than most changes do, which is why `bench-compare.sh` alternates its rounds and tests the difference rather than reading two numbers. For a rewrite of a class, `git show HEAD:<file>` into a scratch copy under another name and run both side by side; the same harness checks the bytes.
- **Split before you guess.** Stopwatches around each stage of one frame found the JPEG costing as much as the picture, which the encoder's own comment said it never would.
- **Ask the JIT what it did:** `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1` works on a Release runtime and lists every method with its tier. `switched MinOpts` next to `CompiledPatch:Whole` means the method was too large to optimize.
- **Real patches need plugins.** `Flyback.Core.Benchmarks` references no plugins, so a user's patch will not open there. The quickest harness is a throwaway test in `Flyback.Plugins.Tests`, which loads the shipped plugins and carries the preset site's defaults (Tranquility among them): see `PresetSiteDefaultsTests.Open`. Run it with the test exe's `-method` and `-showliveoutput`. `src/Flyback.Cli/bin` has no plugins folder, so time the CLI from `dist/<rid>` after `release.sh`. Internal types (`IlEmitter`, `OpShape`) are reached by reflection.
- **Check the bits.** Anything that claims to be a faster route to the same program has to render the same samples. Compare a WAV byte for byte, and let `IlProgramTests` run over every preset.
