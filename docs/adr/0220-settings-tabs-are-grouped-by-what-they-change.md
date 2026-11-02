# ADR-0220: Settings tabs are grouped by what they change

**Status:** Accepted · 2026-11-02 · *user-directed*

## Context

Arek found the Settings pages too busy. Picture had grown to twelve controls on four
topics: how the picture is drawn, where full screen goes, Endless, and five live
patching rows. Canvas put a paragraph of grey text under five of its seven switches,
most of it the switch's tooltip again. No tab had headings, so a row was found by
reading every row above it. He approved the regrouping from a clickable mock.

## Decision

- **A setting sits in the tab named for what it changes.** Picture keeps how the
  picture is drawn. Full screen is a tab of its own, with the monitor, the transport's
  edge and live patching, which is drawn over the full-screen picture. MIDI is renamed
  Playing and takes Endless, since it decides how a patch plays; it already held the
  knob grid and the computer keyboard, neither of them MIDI.
- **The tab list is grouped.** Each section declares its group, and the list shows the
  group's name over its tabs: Picture and sound, Playing and editing, Helpers, and
  Privacy alone under a plain line. A group's name is a row that cannot be picked.
- **Headings cut every tab** into runs of two to five rows.
- **A note under a row is for a consequence**, such as a change that waits for the
  next start or a compact patch that may overlap. An explanation lives in the row's
  tooltip. Privacy keeps its notes, since saying what is sent is the point of the tab.
- The saved settings are untouched: only where a control is drawn moved.

- **A phone taps into a tab.** Where the sheet is narrower than the list beside a tab,
  it shows the list alone, and a tapped row shows that tab full width under a way back,
  each row's name standing above its control rather than in the gutter beside it.
  The tab control stays underneath with its headers hidden, so the two shapes share one
  selection and one Save.

A page's gear panel uses the same headings.
