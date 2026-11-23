# ADR-0239: Hits hears the drums as three gates

**Status:** Accepted · 2026-11-23 · *user-directed*

## Context

People play Flyback as a visualizer for music it does not make, heard through a
Line In (ADR-0178). The first patch a user sent built a kick, snare and hats
detector by hand out of eight Filters, nine Slews and three Expressions, and
every patch of its kind would build it again.

## Decision

**A Hits module, `audio.hits`, in Measurement and on the sound side only.** It
takes a sound and gives `kick`, `snare` and `hats` as gates, and each band's
`level` against its loudest recent hits. It is built from the ops the Filter and
the Slew already emit, so it runs on every backend and in `flyback-cli render
--input`, and needs no new op.

**Bands by filters, not an FFT.** Under `low`, between a little over twice
`low` and a little over half `high`, and over `high`, each through two
twelve-decibel filters, the snare's low edge through three because the kick
spills most. The engine has no FFT (ADR-0019 rules out a package).

**A hit is a sudden rise, judged by ratios only.** A band is hit when its quick
level jumps to 1.6 times its level of a moment before (a follower slow to rise
and quick to fall), to a third of its loudest recent hits, and to a sliver of the
loudest band's (a twelfth for the snare, a fiftieth for the others). Every
threshold is a ratio, so it needs no gain and works at any volume. It must stand
out for four milliseconds, since a drum's first click reaches every band before
the low one has risen to say whose it is. A gate then stays open for `length`,
and the band waits out `rest` and must fall back before it can be hit again, so
one drum is one rise.

A level against a slow average was tried first and missed most kicks under a
held sub; the rise ratio found 79% of the kicks in a neurofunk mix (Torsion)
against 50% for the hand-built detector, with a fifteenth of its false hits.

## Consequences

- A gate is what a tempo follower or an envelope wants, so either plugs in.
- Anything that starts suddenly in a band counts: toms and a bass line in
  eighths are heard as kicks. `sensitivity` trades those for missed hits.
- A kick's sweep starts above `low`, so its gate opens about 20 ms after the
  beater; snares and hats about 8 ms.
