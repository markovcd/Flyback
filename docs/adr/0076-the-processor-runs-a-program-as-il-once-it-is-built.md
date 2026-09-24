# ADR-0076: The processor runs a program as IL once it is built

**Status:** Accepted · 2026-09-16 · *user-directed* · implemented in
`Compile/IlEmitter.cs`, `Compile/IlOps.cs`, `Compile/IlProgram.cs`,
`Compile/IlCompiler.cs`, `Render/SynthRenderer.cs`, `Render/AudioRenderer.cs` and
the `--interpreted` launch flag · takes up the option
[0005](0005-compile-to-a-flat-register-machine.md) declined, amends
[0006](0006-scalar-interpreter-parallel-over-rows.md) and
[0021](0021-recompile-the-whole-patch-on-every-edit.md) · its **Processor** switch
became a launch flag by [0082](0082-the-output-settings-move-to-the-settings-window.md)

## Context

[0005](0005-compile-to-a-flat-register-machine.md) weighed emitting IL against a
switch over an instruction list and chose the switch. It called IL the fastest
per-pixel path and turned it down for two reasons: the result could not be read,
and it could not be retargeted. [0006](0006-scalar-interpreter-parallel-over-rows.md)
then measured the switch and found the loop bound by its own dispatch rather than
by the arithmetic in it.

Both reasons have since stopped applying. The instruction list is the IR and
nothing about it changes: [0035](0035-a-glsl-backend-for-the-video-path.md)
retargets it to a shader, and a second backend beside it is what that record
showed a backend over `Op[]` to be. And the interpreter stays as the thing that
can be read.

What did not go away is what the switch costs. The GPU took it off the preview,
but three things still pay it on the processor: the sound, which runs the whole
program in order once per sample at four times oversampling and has no GPU to go
to; the preview on a machine whose GPU refused; and the preview while somebody has
turned the GPU off to compare.

A prototype in the benchmarks lowered each stage of a program to one
`DynamicMethod`, and answered the two questions that decide whether this is worth
having.

**It gives the same answer.** Every preset, picture and sound, compared to the bit
against the interpreter, with the delay lines, accumulators and cells stepped
together. Nothing differed.

**It costs milliseconds to build.** Lowering and putting four methods through the
JIT takes 1.1 ms for Plasma and 10 ms for Whole band, against 5 µs and 78 µs to
compile the graph to ops. [0021](0021-recompile-the-whole-patch-on-every-edit.md)
recompiles on every mouse move of a knob drag. Paid there, the JIT would stutter
the knob; paid on every edit of a live-coded patch, it would stutter the text.

A patch is performed three ways, and they ask different things of this. A note
played on MIDI changes no program at all — it is a value read through `LoadLive`
([0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md)). A knob
changes one constant. Live coding changes the program's shape on nearly every
edit. None of them may wait for the JIT.

## Decision

**IL is a second way to run the same program, never a second meaning.** `IlProgram`
lowers `CompiledPatch.Ops` — whole, and each stage of the `FramePlan` — to one
method apiece. Every op calls a small method in `IlOps` that is its case in the
switch written a line for a line, and whose guards are the interpreter's own
helpers rather than copies of them. `CompiledPatch.Evaluate` remains the
specification ([0035](0035-a-glsl-backend-for-the-video-path.md)). Registers become
locals; only a register another stage reads, or the output, touches the bank.

**A program plays interpreted the moment it exists, and as IL from whenever that
is ready.** `IlCompiler` builds on a thread of its own, below normal priority, and
attaches the result to the program already playing with `CompiledPatch.Attach`.
`SynthRenderer` reads `patch.Il` once per frame and `AudioRenderer` once per buffer,
and each runs whichever it found. Because the two agree to the bit and share the
program's memory, the hand-over has nothing in it to hear or see. Nothing on the
UI thread, the render loop or the audio callback waits for the JIT, which is what
keeps all three kinds of performance intact.

**A constant is read, not baked.** The code reads each `Const` from an array bound
to one patch, as the shader reads `uK[]`. Built code is kept by *shape* — the ops
with the constants' values left out — so a knob moved by hand finds its code
already built and is bound to it on the UI thread before `Submit` returns.

**Only the latest program per lane is built, and only what that lane runs.**
Picture and sound are separate lanes. A program replaced before the compiler
reached it is dropped unbuilt, so a burst of live-coding edits costs one build
rather than one each. The picture's lane builds the three stage methods and the
sound's builds the whole program, because `SynthRenderer` calls only the one and
`AudioRenderer` only the other, and every method is its own trip through the JIT.
A part that was not built runs on the interpreter rather than failing, so code
built for one renderer is still correct in the other. Code is kept by shape and by
which parts were built, the last 32 of them.

**A build is checked before it is trusted.** It is run against the interpreter at
a spread of points before it is attached; one that disagrees or throws is reported
on the status bar, its shape is not tried again, and that patch goes on being
interpreted.

**The picture's program is only compiled while the processor is drawing it.** The
shader has code of its own, and building IL for a picture nobody's processor draws
would be JIT spent for nothing. The sound's program is always compiled.

**A switch chooses, and it starts on.** The Output panel carries **Processor**,
reading *Compiled* or *Interpreted*, beside the GPU switch it is compared against.
Off takes the IL off the programs playing now, not at the next edit. The status bar
says whether the processor's last frame was *compiled* or *interpreted*, which is
what it actually got rather than what the switch asks for.

## Consequences

Measured on an i5-14600KF, .NET 10, Release, BenchmarkDotNet 0.13.12 short runs,
both arms in one process.

**A frame on the processor**, 960 × 540 through `SynthRenderer`:

| Preset | Interpreted | Compiled | |
|---|---|---|---|
| Plasma | 2.45 ms | 1.18 ms | 2.1× |
| Feedback tunnel | 3.56 ms | 1.91 ms | 1.9× |
| Nebula | 6.93 ms | 4.56 ms | 1.5× |
| Whole band | 8.49 ms | 5.51 ms | 1.5× |

**One audio callback**, 1024 frames through `AudioRenderer` with its decimation
filter:

| Preset | Interpreted | Compiled | |
|---|---|---|---|
| Drone | 441 µs | 241 µs | 1.8× |
| Four voices | 1142 µs | 657 µs | 1.7× |

The evaluations alone, without the filter, run 2.3× faster for Drone, 1.9× for Four
voices and 1.3× for Whole band. The saving is largest where a program is arithmetic
and smallest where it is calls: `Noise3`, a delay line's read and write, and a
sample lookup cost what they cost in either backend.

**What an edit costs:**

| Preset | Graph to ops | IL in stages | IL whole | Both | A knob, rebound |
|---|---|---|---|---|---|
| Plasma | 4.7 µs | 0.76 ms | 0.41 ms | 1.14 ms | 159 ns |
| Nebula | 11 µs | 0.97 ms | 0.76 ms | 1.76 ms | 438 ns |
| Whole band | 76 µs | 5.06 ms | 4.84 ms | 9.98 ms | 3.1 µs |

The two IL columns are what the picture's lane and the sound's lane spend on the
compiler's thread, measured on the same program so they can be compared; *Both* is
what building everything would cost, and no lane asks for it. The last column is
what a knob move adds to the UI thread.

**Reading constants from an array costs the sound some of the gain.** Baked in as
literals, the JIT folds them, and the prototype measured Four voices at 305 µs of
evaluation where the array gives 557. That is the price of a knob drag never
reaching the JIT, and it is paid knowingly. Baking them once a knob has come to
rest, and rebuilding in the background, would earn it back at the price of a third
program to keep in agreement; it is not done.

**A knob does not always keep its shape.** `Emitter.Constant` shares a register
between equal values, so a knob swept through a value another constant already has
merges two ops for as long as it sits there. That is a new shape: the program plays
interpreted until its IL is built, and the cache makes the next pass through the
same value free.

**What cannot yet be felt, cannot yet be heard.** Between an edit that changes the
shape and its IL arriving, the program runs at the interpreter's speed — about five
milliseconds for the largest preset, a quarter of one 1024-frame buffer at 48 kHz.
A patch heavy enough that only the IL keeps up with the audio device would drop out
across that gap. None shipped is.

**One program in both lanes is built twice.** Keeping code by the parts built as
well as the shape means a program the speakers and the screen share in shape does
not share a build. That is rare — the two programs of one patch are rooted at
different outputs — and handing whole-program code to the picture would only put
the picture back on the interpreter.

**Export stays interpreted.** `MovieRenderer` compiles programs of its own that are
never handed to a compiler, so an exported file is the interpreter's by
construction. It would be the same bits either way; it is also the reference, and
keeping it so costs an export nothing it was already paying.

**`CompiledPatch` is no longer entirely immutable.** [0021](0021-recompile-the-whole-patch-on-every-edit.md)
leans on a compiled program never changing under a thread running it. `Il` is the
one field that does, and it is written with `Volatile` and read once per frame or
buffer. What it changes is how an answer is reached, not the answer, so a reader
that sees the old value and one that sees the new draw and play the same thing.

**What the tests hold to.** Every preset is compared to the bit against the
interpreter, picture and sound, whole and staged; every opcode lowers to the
interpreter's answer, and an unknown one is refused rather than skipped; both
renderers produce identical bytes and samples either way; a part that was not built
gives the interpreter's answer; and the compiler attaches a knob synchronously,
builds each lane only what it runs, drops what was replaced, keeps the two lanes
apart, and takes the IL off when switched off. What they do not reach is the hand-over under a real
device while somebody is typing: that was reasoned about, not recorded.

**The switch is not saved.** Like the GPU switch, it is how two backends are
compared rather than a preference, and it starts on every time.

**Amended: the switch is a launch flag.** It moved to the settings window and
was saved there for a while, which is exactly the preference this record said it
was not; [0082](0082-the-output-settings-move-to-the-settings-window.md) took it
out again. `--interpreted` on the command line starts a run that never builds IL
for the CPU's programs and says so on the status bar — the same comparison, and
the same way to rule the compiled code out of a fault, with nothing left behind
for the next launch.

## Amendment, 2026-09-23: offline renders are compiled too

*Export stays interpreted* kept `flyback-cli render`, and the assistant's `render`
and `listen`, at the interpreter's speed for no gain: the IL gives the same bits,
so the file is the reference either way, and a clip or a still of a heavy patch is
where a 1.5-2x saving is felt most. Each of them now builds its programs with
`IlCompiler.CompileOnce` before running them, the picture in stages and the sound
whole, checked against the interpreter as every build is. A program that will not
build is rendered interpreted and said so. `flyback-cli render --interpreted` keeps
a render on the interpreter, for the same reason the launch flag does.

## Amendment, 2026-09-23: a long program is several methods

The sound of a 3,319-op patch (Tranquility, 210 modules) emitted as one method of
53,891 bytes of IL and about 4,400 locals. Past a few thousand locals the JIT
stops optimizing a method: it compiled it `switched MinOpts`, inlined none of the
`IlOps` helpers and kept nothing in the processor's registers, so every op was a
call and the compiled sound ran only 1.26x faster than the interpreter, at 0.96x
real time. The shrinking gain in the tables above, from 2.3x on Drone to 1.3x on
Whole band, was the same thing arriving.

`IlEmitter.EmitChunks` now writes any stretch longer than 256 ops as several
methods, called in order into the same bank. A register one chunk writes and a
later one reads goes through the bank, as it already did between stages, and each
chunk numbers its delay lines and accumulators from where the one before it
stopped. The ops and their order are unchanged, so the bits are too.

Tranquility's sound runs at 2.27x real time where it ran at 0.96x. Chunks of 128,
256 and 512 ops measured the same; 1,024 fell back to 1.13x, so 256 keeps well
clear of the edge. Optimized code costs the JIT more, so the chunks, which share
nothing but read-only constants, go through it side by side on half the cores, at
the compiler thread's priority. Building Tranquility's sound takes 11-14 ms where one
thread took 40, which is the interpreted gap after an edit that changes the shape.
