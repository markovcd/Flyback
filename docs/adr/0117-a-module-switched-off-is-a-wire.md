# ADR-0117: A module switched off is a wire

**Status:** Accepted · 2026-09-20 · *user-directed*

## Context

There was no way to hear a patch without one of its modules. Taking a Vignette
out of a chain meant deleting it and wiring what fed it into what it fed, then
building it again afterwards with its six knobs set back where they were — and
the knobs are gone the moment the module is. So the question every patch asks a
hundred times a session, *what does this sound like without that*, could only be
answered destructively.

Every instrument this one borrows from has an answer. A desk has a channel mute
and an insert bypass; a pedalboard has a footswitch; a plug-in host has a power
button on every slot. What they do not agree on is which of two things it means:
a mute, where the channel stops contributing, or a bypass, where the signal
passes the module untouched. In a rack of boxes wired together these are the same
gesture on different equipment.

Here the two are far apart. A voice switched off should fall silent; a Vignette
switched off should let the picture through. A patch is one graph with both kinds
of module in it, so picking either reading on its own is wrong half the time.

## Decision

**A module can be switched off, and one that is off is a wire.** It is never
entered: what is patched into it comes out of it unchanged, and where nothing is
patched into it, nothing comes out. That is one rule, and it is both readings —
an effect in a chain passes its signal on, a source with nothing feeding it falls
silent — because which one happens is a fact about how the module is wired rather
than about what kind of module it is.

**A socket handed nothing does what an unpatched socket does**: it reads its own
knob, or the module it is normalled to (ADR-0009, ADR-0050). So switching a
module off is pulling its wires out rather than sending silence down them, and a
level knob under a switched-off LFO takes over at the value the panel has been
showing all along.

**Which socket is handed on** is `NodeDef.Through`: the one called `in`, wherever
it sits in the list; failing that the one the output is named after, so a
geometry module hands its `x` to its `x` and its `y` to its `y`; failing that the
first, the catalogue being written with the principal socket at the top. It is
the ladder the language's pipe rule climbs, for the same reason — it is the
question *which socket does a signal travelling through this module travel on*.
The `in` outranks the name match deliberately: an Echo has both an `in` and a
`left`, and its `left` is a delay time.

**A normal is not handed on, only a wire.** Every oscillator's `in` is normalled
to Time, so the other way round a voice switched off would pass a ramp down the
patch instead of falling silent.

**The Output cannot be switched off**, the same way it cannot be deleted
(ADR-0037). Nothing would be left to root the compile at. What was wanted there
is `out.volume`, which is already nought-is-off (ADR-0079).

**It is a field on the instance**, `NodeInstance.Off`, written into the file only
while it is true. No format version: an older build reading a newer file builds
the module on, which is the reading groups already established (ADR-0020's
version is for a file an older reader would get *wrong*, and a module switched
back on is a patch, not a misreading).

**The canvas draws it faint, with its name struck through**, and its wires as
faintly. Not a second color: what the module is has not changed, and the category
accent is how a patch is read at a glance (ADR-0116). The strike is what says it
outright, since a patch drawn small is faint everywhere. A shut box wears the
same marking while every module in it is off, because a box is the one place the
modules cannot say it themselves; an open group is left alone, the strike through
each of them being right there.

**Ctrl+B on the canvas**, the letter a desk uses, and a glyph at the head of the
panel's action row (ADR-0111). One key both ways rather than a pair with Shift,
as group and open have: a module is off or it is on, and there is nothing between
them for a second key to mean. A selection with anything still on goes off, so a
press never leaves it half and half.

**A group is switched by switching its modules.** Pressing a box selects what is
in it, so the key already reaches a whole group; what it was missing is the glyph
on the panel a group gets of its own, which is at the head of that row too. There
is no state on the group itself — a group is a fact about the canvas and the
compiler is never told (see `NodeGroup`), so a second place to be off would be a
second answer to a question the modules already answer. A group is off while
every module in it is, which is the reading a selection has.

**The text language says `off name`**, a statement of its own beside
`name.port = 0.6`. A module that is off is always given a binding when the patch
is printed, because a name is what the statement needs — including a Coordinates
or a Time, which otherwise print as the bare `x` and `t` and have nowhere to say
it.

## Consequences

**Nothing upstream of a switched-off module is compiled.** The walk never reaches
it, so a branch behind one costs the program nothing — the same sweep an unwired
branch already gets (ADR-0011). Switching a heavy chain off is a way to find out
what it was costing.

**A Scope that is off does not tap.** Its whole use is a side effect, so it is
the one module nothing else would have declined for: the taps are collected by
walking the patch rather than by being reached.

**A ring of modules that are all off hands on nothing.** Following the wires
through them has to stop somewhere, and there is nothing at the end of a ring to
arrive at.

**A loop with a module switched off in it is still a loop.** Where the cut falls
is settled once, for the canvas and the compiler alike (ADR-0075), and it is
settled over the patch as drawn — so following the wires through a module that
is off has to carry the cut with it. A wire anywhere along the chain that runs
backwards makes the whole chain carry the evaluation before. Without that the
walk arrives back at a module it is already lowering, and a perfectly good
feedback patch turns into a complaint the moment one module in it goes off.

**Two sockets called the same thing on a module with several outputs is now a
wiring question**, not only a label. A plugin whose `out 2` is named after an
input that is not its signal will hand that input on. An `in` fixes it, and
anything with a real signal input has one.

**A patch can be saved switched off**, which is a state somebody will not notice
they left. The strike through the name is drawn for that as much as for the
moment of pressing the key.
