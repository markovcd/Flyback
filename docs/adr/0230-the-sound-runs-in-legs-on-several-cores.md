# ADR-0230: The sound runs in legs on several cores

**Status:** Accepted · 2026-11-08 · *user-directed*

## Context

The user asked for sound rendered twice as fast. On one core there was no 2x left:
the emitted arithmetic is about half the time, `sin`, `pow` and `tan` a fifth, and
the delay lines' cache misses a tenth. Skipping ops whose inputs did not change
saves at most 36-55% even with perfect knowledge of which ones, before paying for
the guards. A heavy song plays at 2.6-7x real time on one core of a cloud box, and a
phone at a fraction of that, while the other cores sit idle.

Each evaluation of the sound depends on the one before it, so evaluations cannot be
shared out. But within one evaluation, nearly all values flow forward: only a register
read before it is written, and a cell, carry one evaluation into the next.

## Decision

**The whole program is cut into legs, and each leg runs on a core of its own, a
stretch behind the one before it.**

- **What may be cut.** The ops are graphed by what each reads, overwrites and leaves
  for the next evaluation. Each strongly connected component, a knot, has to stay in
  one leg. The knots are laid out in an order their edges allow, nearest the program's
  own (Kahn's sort, first-reached first), each knot keeping its ops in program order.
  Every op computes what it did, so the samples are the ones one core makes, to the bit.
  Lines, cells and remembered answers keep the numbers their place in the program gives
  them. No shipped preset has a knot over about 1% of its work.
- **Where.** Each op has a rough cost, and every register a cut hands on costs extra.
  The cuts start at an even split and move within a window wherever the heavier
  neighboring leg gets lighter. Every count from two up to the cap is tried. An extra
  leg must make the slowest leg 10% faster, and a cut that leaves one leg with three
  quarters of the work, or a program under about 1,500 adds, runs whole.
- **The cap is a setting.** Settings → Sound → Cores, beside Oversampling: Automatic
  (half the machine's cores, up to four, so a phone keeps cool), or 1 to 8. It is one
  number for the whole process (`SoundCores`), and changing it rebuilds the sound that
  is playing. Every flyback-cli command and the viewer read it from the settings, and
  `render --sound-cores` overrides it for one run.
- **The relay.** A leg runs 128 evaluations, a stretch, then hands the registers the
  next leg reads into a ring eight stretches deep. Values that depend only on constants
  and live values are handed on once a buffer. Whichever thread finds a leg ready takes
  it, the rendering thread too, so a helper slow to wake costs speed, never a sample.
  Each leg works on its own copy of the small cells, so two cores never write one cache
  line, and the rendering thread takes back what each leg owns when the round ends.
- **Off the sound's thread.** The relay for a new shape is built on the thread pool.
  Until it is ready the sound plays whole, as it does with a Line In.
- **Shared helpers.** The helper threads are one crew for the whole process: they
  spin briefly between buffers, then sleep.

## Consequences

- Heavy presets on a 4-vCPU box: 1.6-2.5x with four legs, 1.5-2.0x with three; the
  numbers are in the performance skill. A phone has eight cores and gets four legs
  unless Cores says fewer.
- The web viewer has one thread and stays whole.
- The readout says how many cores the last buffer used (`soundCores`).
- More than four legs is untried; each one hands on more and gains less. Cores up to 8
  lets a bigger machine find out.
- `AudioRenderer` is no longer single-threaded inside a buffer. It still allocates
  nothing once built, and a buffer still returns only when its samples are done.
- Amends [0076](0076-the-processor-runs-a-program-as-il-once-it-is-built.md): the IL
  for the whole program is emitted leg by leg, in the order the cut gives.
