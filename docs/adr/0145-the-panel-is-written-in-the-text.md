# ADR-0145: The panel is written in the text

**Status:** Accepted · 2026-09-24 · *user-directed* · amends
[0065](0065-a-text-language-that-parses-to-a-patch.md), builds on
[0086](0086-panel-knobs-are-read-as-live-values.md)

## Context

A patch's knob panel ([0086](0086-panel-knobs-are-read-as-live-values.md)) had no
spelling in the language. The binder could not build a knob and the printer
dropped every one, so a played patch — Dub, Vigil, Irrational, Tranquility —
written out as text and read back was a different instrument, and an assistant
editing one through the text removed its knobs without being told.

The engine keeps a knob and what follows it apart: a `PatchControl` is a
resting position from 0 to 1 and the MIDI controller it follows, and each
socket following it holds a `ControlLink` with its own range and taper. The
proposal in [language-for-agents.md](../language-for-agents.md) gave the knob a
range of its own, which is not what the engine keeps.

## Decision

**A knob is a statement, and a socket follows it by name.**

```
panel cutoff = 0.4, label: "Filter cutoff", cc: 21, channel: 2, device: "midi:elektron-syntakt"

filter(cutoff: cutoff)                        # over the socket's own range
filter(cutoff: cutoff(200..4000))             # over a range of its own
filter(cutoff: cutoff(200..4000, knee: 20))   # and a taper of its own
out.volume = level                            # the Output follows by statement
```

The settings are named arguments, the form every agent in the draft's evidence
wrote fluently, rather than a mini-syntax of their own. A range is read the way
the socket reads a number, so a duration socket takes `20ms..400ms`.

**A knob is a name like a `let`**, bound once and never a word every patch
has. It is not a module's name either, since a knob with a range is written
like a call; the printer turns a clash into `drive_knob`. It is not a signal: it
is not piped or wired, and a socket that follows one takes no number as well. A
sum reads one as a socket of its Expression that follows the knob, so
`t * rate(0..2)` is written the way it is meant. A `def` declares none, since every call would add another to the panel.

**`print` writes the panel first**, and every socket that follows a knob names
it, with the range and knee only where they are not the socket's own.

## Consequences

- Every shipped patch with a panel prints and reads back with the same knobs,
  the same links and the same programs, played and not; `PanelTextTests` holds
  them to it.
- A knob's id comes from its name, so rebuilding the same text keeps the same
  knob, as a module's does ([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)).
- The `panel` lines follow the panel in the text view. Where a knob is left by
  hand is written into its line when the hand comes off it; a controller's turns
  are not, since they would rewrite the line on every message. A knob moved,
  renamed, learned, forgotten, added or removed rewrites the `panel` lines where
  they stand, and a printing is printed again.
- Writing the knobs out found the printer leaving out a knob within 1e-7 of its
  default; only one exactly at its default is left out now.
