# A debug pass that reports every output's value

Planned on 2026-10-01, on `main` at `d79bd24f`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

While building a patch, the user should know exactly what comes out of every output socket.
For each one, and for each of r, g and b on a color:

- its value, when it is static;
- whether it is static or changing, always with the window it was watched over ("no change
  in 4 s");
- how fast it changes: the dominant frequency when it repeats, otherwise the steepest slope a
  second, or steps a second for a stepped signal (a sequencer, a Sample & Hold);
- its range when it changes: min, max and mean.

Any output on any module, wired to anything or to nothing. Limiting it to outputs the speakers
reach was proposed and turned down, because a module being built is usually not wired yet.

## Two values per socket

The speakers compute a socket over time only, with memory. The screen computes it over x, y and
t, with none (ADR-0040, ADR-0053). They disagree wherever the patch has memory, and a value can
be static over time while varying across the picture (anything reading Coordinates). The report
gives both halves and flags where they differ rather than picking one.

## Shape: one pass, then a report

A pass runs the patch offline for N seconds from the playhead, with the knobs where they are,
stops, and reports. It is not real time and nothing runs before or after it, so it costs only
the wait.

1. **`flyback-cli watch <patch> [<module>.<socket>] [--seconds N] [--from T] [--json]`.** Built
   first, on a shared measurement (say `SocketWatch`), so its numbers are tested before any
   window shows them. Every output when no socket is named.
2. **The editor, desktop only.** A button and a shortcut run the same pass, with a progress
   bar, and pin a reading beside every output socket on the canvas: `0.25` when static,
   `-1..1 · 2 Hz` when changing, a swatch and r, g, b for a color. Hovering one gives the full
   report, and the selected module's report sits under each output's row in the inspector
   (`Inspector.BuildOutputRow`). An edit dims the readings and marks them out of date until the
   next pass; nothing is saved in the patch.
3. **A workbench `watch` tool** for the assistant, on the same measurement.

## How it runs

The obstacle is not that a patch is compiled. An output is unreadable today for three reasons,
and the pass answers each:

- **Nothing reached, nothing lowered.** The compile walks back from a sink (ADR-0011), so an
  output nothing reaches has no ops. The pass's sound program roots at every output of every
  module, so a shared chain is lowered once.
- **The socket-to-register map is dropped.** `PatchCompiler` builds it (the `resolved` memo) and
  throws it away; `CompiledPatch` keeps only the Output's registers. The pass's compile keeps it.
- **Backends hide registers.** IL turns them into locals (`IlEmitter`) and GLSL never hands
  values back. The interpreter leaves every register in one array (`AllocateRegisters`), so the
  pass runs there and reads every output after each evaluation. No `Tap` op is needed, and the
  interpreter's speed does not matter off the clock.

Then:

- **Sound:** evaluated at the real rate with its own `DelayState`, exactly as the speakers
  would, keeping running totals per component (min, max, mean, last, crossings, steps).
  Memory starts empty, so a reverb or a long delay is reported as filling over its own length.
- **Picture:** one program rooted like `CompileForProbe` but at every output, evaluated on the
  interpreter over a coarse grid (about 32×18) at frames across the same window. Spread within a
  frame is variation across the picture; change per pixel between frames is change over time.
- **A long window is fine.** 30 seconds catches a slow LFO; it only takes longer.

## Limit

An offline pass cannot see what is played: MIDI notes and a panel knob turned during the window
are not in it, so an envelope a key fires never fires. Drive the patch from inside it (a
Sequencer, a clock) to measure that. A later pass that records the next N seconds of live
playing and reports after is possible; ship the offline one first.

## Open

- A name for it. The glossary takes its words from the instrument; whatever is chosen goes
  there in the commit that lands it.
