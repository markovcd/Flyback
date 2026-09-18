# ADR-0101: A one-knob maximizer is a module of its own

**Status:** Accepted · 2026-09-18 · *user-directed* · follows
[0100](0100-mastering-is-a-plugin-of-stateful-primitives.md); raises the default
of [0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md)

## Context

FL Studio's Soundgoodizer made a mix louder and denser with one knob and four
modes, and was popular for how little it asked. Underneath it was a multiband
compressor and a limiter. The Mastering plugin
([0100](0100-mastering-is-a-plugin-of-stateful-primitives.md)) has every piece:
two Crossovers, three Compressors, a Desk and a Limiter make one. That is eight
modules and some twenty knobs, and a knob of 'amount' cannot reach inside a
group and move three thresholds at once.

A module standing for a handful of others is what
[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md)
governs, and it asks for two things: the module must be exactly those modules,
and the pattern must already be in the presets. No preset builds a multiband
chain, and a one-knob module is not exactly any wiring of the eight, because
its settings are worked out from the knob.

## Decision

**The Maximizer is a module of its own, not a wrapper.** It claims to be no
wiring of other modules, so it owes no exactness to one, and 0095 does not
apply. It reuses their DSP where that DSP lives: the Crossover's split, the
Compressor's gain computer and the Limiter's limit are functions the three
modules and the Maximizer all call.

**One knob, one style.** 'amount' brings every band's threshold down, by as much
as 24 dB, and applies the makeup and the style's tilt in proportion. The bands
split at 150 Hz and 2.5 kHz, each band is compressed with both sides linked,
and the sum is limited to -1 dB. Four styles set the ratios, times, tilt and a
touch of saturation: glue, punch, bright and loud. 'style' is a socket and not
a setting, by [0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md)'s
rule, because it changes the numbers the ops are given and not which ops there
are. The styles are voiced by ear, and the module takes neither
Soundgoodizer's name nor its curves.

**'amount' is not a wet/dry mix.** The crossover turns the phase and the
limiter delays the sound, so the dry signal mixed back in would comb-filter. At
nought the thresholds sit at full scale and the tilt is flat, and what is left
is an allpass, a delay of one lookahead and a ceiling.

**The briefing's default budget is 100,000 characters.** What ships had
reached the four fifths of 80,000 that 0098 holds it to, with the six Mastering
descriptions already cut short to fit. Every module now makes the budget
smaller, so the default goes up, and what ships comes back to about two thirds
of it. A budget someone has saved is theirs and does not change.

## Consequences

A patch can be made loud with one module and one knob. The Maximizer is costly
to evaluate: two crossovers are twenty state-variable filters, three
compressors and a limiter, which is more than most voices. The briefing has
room again for the next module and for somebody's plugins.
