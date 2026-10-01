# A debug mode that shows every output's value

Planned on 2026-10-01, on `main` at `d79bd24f`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open, parked

## What is wanted

While building a patch, the user should know exactly what comes out of every output socket.
For each one, and for each of r, g and b on a color:

- its value, when it is static;
- whether it is static or changing, and over what window it was watched;
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

## Shape

1. **`flyback-cli watch <patch> [<module>.<socket>] [--all] [--seconds N] [--json]`.** Built
   first, on a shared measurement (say `SocketWatch`), so its numbers are tested before any
   window shows them.
2. **The debug mode in the editor, desktop only.** One toggle (a toolbar button and a shortcut),
   not saved in the patch. The status line says Debug while it is on, as it does for a Probe.
   - A small reading beside every output socket on the canvas: `0.25` when static,
     `-1..1 · 2 Hz` when changing, a swatch and r, g, b for a color. Hovering one gives the full
     report.
   - The selected module's full report in the inspector, on a line under each output's row
     (`Inspector.BuildOutputRow`).
3. **A workbench `watch` tool** for the assistant, on the same measurement.

## How it runs

- **A measuring sound program, separate from the speakers'.** Compiled with every output of
  every module as a root, so a shared chain is computed once, on a worker thread with its own
  `DelayState`. It reads the live clock, panel knobs and MIDI voices by name, so it sees what the
  live patch sees. The speakers' program is never recompiled or slowed by it. Rooting at a
  socket generalizes what `NodeDef.TapsSignal` and `OpCode.Tap` do for the Scope; `Tap` writes
  component 0 only today (`Emitter.cs`), so a color needs three.
- **Running totals, not rings.** The worker keeps min, max, mean, last value, crossings and
  steps per component, and the readings poll them a few times a second.
- **The picture half on the processor.** The GPU never hands values back, so one program
  rooted like `CompileForProbe`, but at every output, is evaluated over a coarse grid (about
  32×18) a few times a second. Spread within a frame is variation across the picture; change
  per pixel between frames is change over time.
- **Recompiled with every edit** while the mode is on, beside the live programs.
- **Memory starts empty** when the mode turns on: a reverb or a long delay reads as warming up
  until it has run for its own length. A worker that falls behind says so ("measuring at
  0.6×") rather than reporting stale numbers as current.

## Cost

Nothing while off. While on, the whole patch runs a second time on another core, plus the
picture grid. Updating the readings in place, never rebuilding the inspector, keeps the window's
side negligible.

## Open

- A name for the mode. The glossary takes its words from the instrument; whatever is chosen
  goes there in the commit that lands it.
