# ADR-0050: Normalled sockets carry a signal with no wire

**Status:** Accepted · 2026-08-24 · *user-directed* · retires the warning
[0011](0011-compile-backwards-from-output.md) raised about an unmoving domain,
and takes the knob off the socket [0009](0009-editable-defaults-on-every-input.md)
put one on

## Context

Two wires are drawn in almost every patch that has ever been built here, and
they are the same two every time.

**Time into `in`.** Every oscillator and every sequencer has a domain socket,
and [0030](0030-oscillators-accumulate-their-phase.md) is why: an oscillator
advances by how far its `in` moved, so one whose `in` does not move produces a
fixed value. Left alone it is silence at the speakers and a flat field on the
screen. Most of the presets wire a Time into it; so does every patch an
agent has built that worked.

**Coordinates into `x` and `y`.** Every Space module, every Pattern module and
the Feedback module take a position. At the default of (0, 0) each reads the
single point at the centre of the picture — a Rings that is one value everywhere,
a Feedback sampling one pixel. Every preset wires Coordinates in.

Neither wire is a decision. They are the cost of the first thing anyone does with
the module, and paying it has three separate prices:

- **It is the one mistake that reads correctly.** A patch missing the Time wire
  compiles, renders, saves and opens. It is a still picture or a silence, and
  nothing in it is wrong — the knob says 440, the wire is right, and the sound is
  not there. The compiler was made to complain about it
  ([0011](0011-compile-backwards-from-output.md)) precisely because nothing else
  could, and a warning is a poor substitute for the thing not happening.
- **It is worst for the agent.** [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md)'s
  assistant cannot see a still picture as still or hear a silence as silent. The
  handbook spent five paragraphs on this one socket, and it was still the most
  common way for a run to end with a patch that read perfectly and did nothing.
- **The knob under the socket is a lie either way.** A number on a domain is
  never what anybody wanted: it is a module standing still, which is a thing you
  might mean once and never by leaving a control where you found it.

A rack has an answer to this, and it is not a default. An unplugged jack there is
already carrying something — the multiple below it, the bus behind the panel —
and plugging in interrupts it. The Output's `right` has worked this way here
since it existed: unpatched, it carries `left`.

## Decision

**A socket may be normalled to a module, and every domain socket is normalled to
Time.** `PortSpec.NormalledTo` names a module by type id and one of its outputs.
While nothing is patched into the socket, the compiler emits that module in its
place. Patching overrides it; unplugging brings it back.

**Every `x` and `y` that means a position is normalled to Coordinates.** The
eight Space modules, the three Patterns and Feedback. A Rotate placed and left
alone rotates the picture, which is what placing a Rotate meant.

**The module behind a normal is hidden and shared.** There is no node on the
canvas, so there is no wire to draw and none is drawn. One hidden Time serves
every socket normalled to it, one hidden Coordinates serves every socket
normalled to that, and each is emitted once per program — a patch with eight
oscillators in it loads the clock once. It carries no knobs of its own: nothing
exists for anybody to have turned one on, so it is emitted with its definition's
own defaults.

**A normalled socket has no knob.** The stored value is not read while the normal
holds, so the inspector shows what is driving the socket where the slider was,
the node shows the module's name where the number was, and the assistant's
`set_knobs` refuses with the reason. A patch that wants a constant on a domain
patches a **Value** in — the same trade [0048](0048-time-is-seconds-and-nothing-else.md)
took for a scaled clock, and for the same reason: the constant becomes something
the patch says rather than something a socket hides.

**A normal names a type id, not a definition.** So a plugin can normal its own
socket to `time` — the Supersaw does — and a normal that names a module the
running catalogue does not hold falls back to the knob rather than to silence.
That fallback is also where the old complaint still lives: a domain normalled to
nothing is remarked on exactly as it always was.

## Consequences

**Nothing renders differently, before or after the presets were rewritten.** All
twelve preset snapshots and all twenty-four approved shaders are byte-for-byte
unchanged — first because every preset drew both wires by hand and an explicit
wire still wins, and then again with those wires gone, down to the register
numbering. The second of those is the stronger check: the hidden module is
emitted at exactly the point in the walk the module on the canvas was, so a
preset that draws the wire and one that does not are the same program and not
merely the same picture.

**The presets say it the new way.** Every preset was rewritten to drop what it no
longer has to draw: **18 modules and 55 wires** out of seventeen patches, and
not one op out of any program. Fourteen of the seventeen changed. Nine lost a source
module outright — Sequence lost both its Time and its Coordinates and is now
eleven modules of nothing but the thing it does.

The alternative was to leave them alone, on the argument that a preset is read as
much as it is run and a Time with a wire out of it is where somebody learns where
the clock comes from. That was rejected, and the reason is the same one that
motivates this record: a preset is the worked example, so it has to be written in
the idiom the instrument actually has. Nine presets each drawing the identical two
wires teaches that those wires are the price of entry, which is exactly what
stopped being true.

What survives is the wire that is a decision, and every preset now shows only
those. Plasma keeps both its Coordinates wires because they are the patch — a
sine read across x and another across y — and Four Voices, Chromatic and Kick keep
a Coordinates for `radius`, which is the one output nothing is normalled to.
Drone, Echo chamber and Whole rack keep a Time for a Rings' `offset`, which is the
socket that has to be told to move. Reading a preset now, every source module in
it is there because something in that patch needed a source, and that is more
legible than the uniformity was.

**A warning was retired.** "Nothing is wired into X's 'in', so it never moves" is
unreachable for every module in the catalogue. It survives for a domain that is
normalled to nothing, which now means a plugin's socket or a patch opened without
the plugin that wrote it. Three of the assistant's tests were about that warning
and are now about a cycle instead — a fault only the speakers reach, which is
what those tests were really guarding.

**A knob became unreachable, and one patch shape got harder.** Standing an
oscillator still now takes a Value module where it used to take leaving a socket
alone. That is a node in a patch nobody builds often, against a wire in every
patch anybody builds at all, and it makes the deliberate case look deliberate.
Old files keep whatever number was stored on the socket, unread — the same
tolerance [0020](0020-json-patch-files-keyed-by-string-type-ids.md) already gives
a value whose socket has gone.

**A patch is no longer entirely visible on the canvas.** This is the real cost,
and it is charged against [0004](0004-visual-patch-editor-as-the-authoring-model.md):
a signal now arrives from somewhere the picture does not show. What is bought
back is that the two wires it hides are the two nobody was reading anyway — a
canvas where every oscillator trails a line to the same Time module is a canvas
that shows where the clock is by drawing it eight times. The editor says which
module is driving each socket, in the place the number used to be, and the
inspector says why there is no wire.

**Deciding where a normal belongs is now a design question with a wrong answer.**
Time on a domain and Coordinates on a position are safe because the module is
useless without them. `z` on Noise and `offset` on Rings were considered and
left: a still noise field is a thing people want, and normalling those would take
that away to save a wire nobody draws by reflex. The rule is that a normal is for
a socket whose only sensible source is the one thing — not for a socket whose
common source is.
