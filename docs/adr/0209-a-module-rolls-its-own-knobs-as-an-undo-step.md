# ADR-0209: A module rolls its own knobs, as an undo step

**Status:** Accepted · 2026-10-17 · *user-directed* · beside
[0170](0170-the-knob-panel-randomizes-and-a-held-knob-sits-it-out.md)'s panel die;
implemented in `src/Flyback.Editor/Knobs/ModuleDie.cs`

## Context

The panel's die moves only knobs somebody has put on the panel. Exploring one
module, a Filter or a Feedback, meant turning each of its sockets by hand. The
user asked whether a decision model could pick a module's knobs from the whole
patch and offer several settings to cycle through. It cannot pick a number, only
rank options it is handed, and it reads neither the picture nor the sound. Rolling
each module's knobs six times across 49 presets and rendering every roll found 4%
broken (black, flat, clipping, silent), so a filter on renders would rarely change
what a plain roll gives, at the price of playing the patch's live state forward for
every candidate.

## Decision

**Ctrl+Shift+D, or the die on a module's inspector, rolls the selection's own
knobs.** Each unpatched input that rests on a knob of its own goes somewhere within
the panel die's amount of where it is, swept as its slider sweeps, in decades above
its knee; a stepped one lands on a whole step. A wired input, or one on a panel
knob, stays where it is.

**A roll is one undo step.** A socket's value is the document's, unlike a panel
knob's live value (0086), so Ctrl+Z steps back through the rolls and Ctrl+Y forward
again: that is the cycle through options. The text is written back as a turn on
the canvas is.

**No model and no render choose the roll.** It is the die alone.

## Consequences

- The panel die and the module die share the amount; each has its own key.
- `Used.RolledModule` counts a roll.
