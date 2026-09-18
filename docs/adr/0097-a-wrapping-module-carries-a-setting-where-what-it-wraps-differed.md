# ADR-0097: A wrapping module carries a setting where what it wraps differed

**Status:** Accepted · 2026-09-18 · *user-directed* · follows
[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md) and
[0055](0055-a-plugins-extra-declares-its-editor.md)

## Context

[0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md) found
six things the showcase presets built over and over and made each a module. It
counted patterns that were identical, and left the rest: a wrapper that fits
three presets of four is a wrapper the fourth cannot use.

A second count, of every wire in all thirty-six presets, found that most of what
was left is identical but for one decision. Four tracks close with two Delays
whose times are a number of sixteenths divided by the tempo; three feed the
second from the first and one feeds both from the input. Sixteen times a plane
goes through a Rotate and a Scale; seven go one way round and nine the other,
and the two orders differ in the last bit. Thirteen parts are a Random through a
Filter into a Multiply by an envelope, and what differs is which noise and which
of the Filter's three outputs. None of these is a signal. Nobody patches the
order of two modules from an oscillator.

[0055](0055-a-plugins-extra-declares-its-editor.md) already gave a module a place
for what is not a knob — a Layer's blend mode, a Fractal's octaves — declared as
fields the inspector draws and the text language writes as arguments.

## Decision

**Seven more modules that are each a handful of others, and two outputs.** Echo
in Effects; Hiss and Bell in Voice; Transform, Ink, Vignette and Tune in the
engine, because what they wrap is the engine's. A Euclid has a `stroke` and a
Tempo has `beats`, since each was one op away from what was being patched after
it every time. 0095's condition holds for all of them: the arithmetic of the
modules they stand for, in their order, with a test beside each that builds the
long form and compares with equality.

**Where the long forms differed by a decision, the decision is a setting.**
`SettingsExtra` is a key and a list of fields and nothing else, for a module
with no more to say about its settings than what they are. The rule for which
side of the line a thing falls:

- A **socket** where the value changes what the ops compute. An Echo's times,
  a Hiss's cutoff, a Bell's ratio.
- A **setting** where it changes which ops are emitted. Whether an Echo's second
  tap hears the first or the input; whether a Transform zooms before it turns;
  white or pink, and low, band or high, in a Hiss; whether an Ink is added or
  laid over. A signal cannot choose between two programs, and a module that
  emitted both and mixed them would pay for the one not wanted — pink is
  thirteen noise lookups — and would be exact in neither, since a Mix at either
  end is its operands only nearly.

Each setting's default is the commoner form, and the other is what makes the
module fit the presets the default does not.

**A Hiss makes its own noise, and it is the same noise.** The presets gave one
Random to several Filters. Random's white is a hash of the clock and the seed
with no memory, so a Hiss with the same seed is handed the samples the shared
one gave: the parts moved onto it play what they played, and the Random goes.
This is the module 0095 found and did not keep, with the two things added that
make it more than Random's first output.

**Counted again: the Bell.** 0095 left the two-sine bell out at nine instances of
ten in one preset. It is twelve in four now, and `PresetBench` had named it.

**Counted and left out.** A gate that is one where two step counts are equal
(five, in two presets). Lines from the fraction of a coordinate less a half
(one preset). A sidechain, which is a Remap into a Multiply, and a Rings into a
Smoothstep: each is two modules and would be one, which is not worth a name. The
arrangement's Sequencer into a Slew, whose steps a wrapper could not hide. Time
multiplied by a speed, twenty-six times, which a knob on Time would not shorten
because one Time feeds them all —
[0048](0048-time-is-seconds-and-nothing-else.md) again.

## Consequences

**The nine showcases are 1,450 modules, from 1,586,** and every one plays the
samples and draws the bytes it did: each was compared with itself as it stood
before, over six half-second windows of sound and four runs of frames.

**What a wrapper saves in modules it can cost in ops.** Three Hisses work out
white three times where one Random did it once, and a Transform subtracts a
nought the Rotate and Scale it replaced did not have. Outrun's sound is fifteen
ops longer of two thousand. The sweep of
[0096](0096-an-op-nothing-reads-is-left-out.md) cannot take these out: they are
read.

**New sockets go last.** Tempo's `in` and Euclid's `curve` are appended, so a
patch saved before them finds its knobs where it left them and rests the new
ones on their defaults. Tempo's output is still beats a second on the first
socket.

**No preset counts its beats off the Tempo yet.** `PresetBench` wires the first
output of whatever it is handed, and the count is read in some twenty places a
track. It is there for a patch made by hand, which is where Time into a Multiply
by the Tempo was the first three modules placed.

**The engine's catalogue is at the edge of its handbook.** The assistant is
briefed in prose while the prose fits
([0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)), and with
four more modules the engine's own catalogue fits with eighty-five characters to
spare. The descriptions of the four are as short as they are for that reason,
and the next module added to the engine will have to take the budget up or take
something out. With the plugins loaded it was over already.
