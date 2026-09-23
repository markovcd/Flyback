# ADR-0142: Figures is three modules that are each a picture and a sound

**Status:** Accepted · 2026-09-23 · *user-directed* · builds on
[0074](0074-a-cell-is-a-plane-on-the-video-path.md) for planes,
[0118](0118-a-plugin-paints-its-own-module-background.md) for artwork, and
[0141](0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)
for how it reaches anybody

## Context

The slogan is one patch, a picture and a sound. Most modules are one or the
other, and the ones that cross over do so by reading: a Scan hears a field, a
Probe draws a signal. Nothing yet is one formula whose value on the screen and
in the speakers is the same physical thing seen and heard.

A module that rings after a strike needs to know how long ago it was struck.
A cell reads zero on the video path, so a plugin has had no way to keep time
on both sinks at once; planes were designed for exactly that and no module used
one.

## Decision

**Three modules, each one lowering for both sinks, in the three directions the
slogan allows.** Plate is a struck rectangular plate: the sum of its modes'
decaying cosines is the sound, and the sand thrown by the same modes'
acceleration is the figure. Harmonograph is two damped pendulums each way: the
pen's path is the drawing and the pen at pitch is the chord, from one ratio,
one twist and one damping. Overtones reads whatever feeds `spectrum` along a
row of the screen, one place per partial, as the heights of an additive tone,
and draws the readings back as bars and a wave. Vigil, the plugin's preset,
is dark ambient on all three: four Overtones reading the fog on the screen are
the drone, the harmonograph swells and draws the chords, and plates are a far
bell and a knell.

**A strike is kept in planes, and the clock in a plane is wrapped at sixteen
seconds.** A plane write is clamped to ±16 like a cell's, so the clock cannot
be stored raw: `Strike` keeps the clock modulo sixteen at the strike, the
trigger as it was, and the velocity it landed with, and the age is the floored
difference, which is in [0, 16) whichever side of the wrap the two fell. Past
fifteen seconds the held level is dropped to nought, so a wrapped age is
silence rather than a second strike, and a plane never written reads as never
struck. On the screen this is a value per pixel and on the speakers a cell, so
the same age serves both. The harmonograph keeps the last frame's clock the
same way, since the emitter's interval is a cell the screen does not have, and
its ink in a fifth plane.

**Sand goes by acceleration.** A mode's acceleration goes as its frequency
squared, so a strike clears the plate, high modes hold it clear while they
ring, and the figure comes back along the lines of whichever modes are left,
the lowest mode's last. The thresholds are two constants in units of the
lowest mode's fullest swing; the level is in every envelope and drops out of
the figure's shape, and a harder strike keeps the plate clear longer. The
figure is not the root-mean-square of the swing, which is bright at the
antinodes and never settles: that is the module's `motion` output.

**Overtones is a Probe the other way about.** It pushes a place that varies
along the partials and resolves `spectrum` once per partial, so whatever of
what feeds it depends on the place is lowered that many times
([0143](0143-a-module-a-sweep-reads-is-lowered-once-wherever-it-reads-the-same.md)).
The count is a setting on the node, and the description says what each
partial costs. Plate lowers its strike and its ring once however many places
it is read at, so only where the place sits on the plate is paid per partial.
The partials share one phase, each turned from the one below by the
fundamental's angle, so a partial costs no sine of its own.

**Each module wears an artwork of what it is**: a nodal figure with sand, a
harmonograph drawing, a row of partials, as SVGs embedded in the assembly, one
for the block and one for the panel, drawn a shade under the node gray so white
labels read over them.

## Consequences

- The first plugin to use planes, and the first to use `Artwork`. `Plate`
  keeps three, `Harmonograph` five; on the speakers' program a plane write is a
  root the compiler keeps, so the ink is written to a cell nobody reads, about
  thirty ops a sample.
- A plane holding the clock wraps at sixteen seconds, so nothing rings longer
  than fifteen. A half-float render target holds the wrapped clock to about
  a sixty-fourth of a second near the wrap.
- Op counts, on the speakers: Plate at nine modes about 280, Harmonograph
  about 130, Overtones about fifteen a partial besides what it reads. Vigil
  is about 1,900, a Noise3 of fog in each of its 32 partials.
- There is no Gherkin scenario: the specs project reaches only the engine.
  `PlateTests`, `HarmonographTests` and `OvertonesTests` state the
  requirements in their names.
