# ADR-0051: A quantiser's scale is a set on the node

**Status:** Accepted · 2026-08-25 · *user-directed* · generalises
[0038](0038-a-sequencers-notes-are-a-list-on-the-node.md) from one exception to
a rule, and adds a second field to the storage
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) describes

## Context

A pitch quantiser snaps a signal to the nearest note of a scale. The scale is
twelve switches — which of the chromatic pitch classes are in it — and it was
asked for as switches rather than as sockets.

That request is the whole design question, and it is the one
[0038](0038-a-sequencers-notes-are-a-list-on-the-node.md) already answered for a
sequencer's tune. Twelve inputs would be a module nobody can read, and worse than
unreadable: an input is a thing a patch may drive, and none of these twelve is.
Which notes exist in a piece is a decision *about* the piece rather than a signal
*in* it. There is no sensible wire to run into "is there an F# in this".

So it is instance data. That leaves two questions, and the second is the one
worth recording.

**Could it reuse `Steps`?** A node already carries one list that is not a knob.
A scale of seven notes is a list of seven numbers, and `Step.Value` would hold
them. But a tune is ordered and may repeat a note, and a scale is neither — two
scales with the same notes in them are the same scale. A `Step` would also carry
a length and a volume that mean nothing here, and the editor for one is a list
you add to and reorder, which is the wrong gesture for twelve things that are
either in or out.

**What does the scale cost to compile?** The nearest note of pitch class `p` to a
signal `n` is `12·round((n − p)/12) + p` — the octave that puts `p` nearest,
which is a rounding rather than a search. So each note in the scale is one
candidate, and the answer is whichever candidate the signal is least far from.
A note that is switched *off* contributes nothing at all.

## Decision

**A node may carry a scale: `NodeInstance.Scale`, a set of pitch classes.** Null
for every module that has none, exactly as `Steps` is, and declared by
`NodeDef.DefaultScale` in the same way `DefaultSteps` declares a tune. A fresh
Quantiser carries a major scale, because chromatic is a module that arrives doing
nothing.

**It is a set, and is made one on the way in.** `Pitch.Scale` puts it inside the
octave, drops repeats and sorts it. A file is text somebody may have edited, so
the shape it can hold is wider than the shape that means anything — the same
tolerance `Step.Sane` gives a note. Sorting is not tidiness: it makes the twelve
switches the whole of the state, and it settles ties upward.

**The scale reaches the emit function as values, not registers.** `EmitContext`
gains `Scale` beside `Steps`, and for a stronger reason than `Steps` has. A
sequencer folds its lengths at compile time because it *can*; a quantiser must,
because the scale decides how many ops the module lowers to. Both ends of the
range collapse — all twelve is `floor(n + 0.5)` and none at all is a wire — and
neither is expressible if the twelve switches are registers.

**It is edited as a keyboard.** Twelve toggles laid out as an octave, sharps
above and between the naturals. The layout is the point rather than decoration:
the gaps where E–F and B–C meet are how an eye finds a key without reading the
label, and C major is a picture before it is a list of numbers.

**The assistant gets `set_scale`,** which takes the whole set in one call for the
reason `set_steps` does: twelve calls to switch twelve notes is twelve chances to
stop halfway. It names pitch classes 0–11, and refuses a note number by name —
60 for middle C is exactly the mistake a model steeped in MIDI will make.

## Consequences

**`EmitContext.Scale` is an init property rather than a constructor parameter.**
That type is part of what a plugin is written against, so a positional parameter
would break every plugin's tests at the source level. `NodeDef.DefaultSteps` is
an init property for the same stated reason, and this follows it. The getter
answers with an empty list rather than null, so an emit function may read it
without asking.

**The file format did not move.** A new optional field an older reader ignores is
not a change of shape — the same rule that says adding a module does not raise
`FormatVersion`. A patch is serialised straight from the object graph, so the
field costs nothing in any file that has no quantiser in it.

**The pattern is now a rule rather than an exception.** 0038 recorded a
sequencer's tune as "the one thing an instance carries that is not a knob". There
are two now, and the next one will be easier to argue for than either — which is
worth being wary of. The test each has passed is the same and should stay the
test: the data is a decision about the piece rather than a signal in it, *and*
there is no arrangement of sockets that expresses it. A control that fails the
second half is a knob, and knobs already exist.

**An empty scale is a wire, and nothing prevents one.** Every switch may be
turned off, and what comes out is what went in. The alternative — refusing the
last one — would make the twelve switches unequal, and the state is reachable
from a file regardless. The panel says which of the two odd ends it is in, since
both look from the keys alone like an ordinary scale that happens to be full or
empty.

**A scale has no root.** Twelve switches say which notes exist and nothing says
which is home, so "D minor" is not a state the module could show you it was in —
only the seven notes that happen to spell it. That is a smaller thing than a root
and a mode, and it is the thing that was asked for; a root would also be the
first control here that changes what every other one means.
