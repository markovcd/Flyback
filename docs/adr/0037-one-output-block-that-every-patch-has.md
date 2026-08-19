# ADR-0037: One Output block, which every patch has

**Status:** Accepted · 2026-08-17 · *user-directed* · amends
[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md), supersedes its
one-of-each rule; changes the file format written by
[0020](0020-json-patch-files-keyed-by-string-type-ids.md)

## Context

[0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) gave the synth two
sink modules, `video.output` and `audio.output`, either of which a patch might
have or not have. That was right about compilation and it cost more than it
looked, in two places that have nothing to do with each other.

**The toolbar.** Everything about seeing or hearing a patch ended up along the
top of the window, because there was nowhere else for it: preview resolution,
the GPU switch, *Save frame…*, *Audio on*, the export length, *Render audio…*,
*Export video…*. Nine controls in one row, arranged by nothing, and each of them
acting on a patch through a sink the toolbar could not name — the resolution knob
sat next to a button that used it and a button that did not. Adding video export
made a crowded bar into an unreadable one.

**The graph.** "Either sink may be absent" is a rule that has to be answered
everywhere: the compiler decided when a missing sink was worth remarking on, the
palette greyed out whichever was placed, the assistant refused a second and had
to be told to add a first, `Patch.CanAdd` existed for it, and every preset wired
two nodes that were always drawn a few hundred pixels apart. It bought the
ability to build a patch with no picture, which nothing wants: a patch with
nothing in its `color` is exactly the same thing and needs no rule.

## Decision

**One `output` module, on every patch, always.** It carries `color`, `left`,
`right`, `gain`, `scan` and `scan rate` — the two old sinks' sockets on one
block. It cannot be added, because there is already one; it cannot be deleted;
and it is not in the palette, since a button that can never place anything is a
puzzle rather than a feature.

`Patch.EnsureOutput` is what makes "always" true, and it is called on every route
a patch takes into the program: read from a file, adopted by the assistant's
workbench, or assigned to the editor. `Patch.Remove` refuses the sink and reports
that it did.

**Both programs still root at it, and still pay only for what they reach.**
That was the whole of 0022 and it survives, but it no longer falls out of there
being two nodes, so it is said explicitly: a `SinkKind` names which of the
Output's *sockets* a program walks back from as well as which of its results it
reads. The screen resolves `color` and nothing else; the speakers resolve
`left`, `right` and `gain`. Anything patched into the other half is never
visited and emits nothing.

**The shell hangs every audio and video setting off that block's panel.** The
toolbar keeps what acts on the patch as a document — the preset list, open, save,
play, rewind, the assistant — and the Output's inspector gets the rest, under
*Picture*, *Sound* and *Export*.

## Consequences

The file format changes and no old patch opens as it was. Both sink type ids are
gone; a file naming them now reports an unknown module, as
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) says it must. This was
accepted deliberately rather than worked around — the alternative is a rewrite
rule mapping two nodes onto one and merging whatever was wired to each, which is
more machinery than the handful of saved patches in existence are worth.

Nothing in the settings panel is saved with the patch, and that is the point
worth being explicit about. A preview resolution and a choice of renderer are
properties of the machine you are working at, not of the instrument; what the
panel gives them is somewhere to *be found*, next to the block they act on.
`gain`, `scan` and `scan rate` are the opposite — they are the patch, and they
sit in the same panel as ordinary knob rows above the settings.

The video program carries two dead multiplies. The sink's emit function computes
all three results, so a program that reads only the first still emits the other
two against a zero constant. Keeping that is a deliberate trade: the alternative
is the compiler knowing what an Output *does*, which is exactly what
[0008](0008-modules-as-data-in-one-catalogue.md) exists to prevent. Two ops
against a program of eighty, and everything upstream of the unused half is still
never visited, which is where all the cost was. The GLSL snapshots record them.

The compiler has one complaint about the sink where it had three. There is no
longer a patch with no output, so that message is gone from the ordinary path —
it survives only for a graph assembled by hand, which is the one way to build a
patch that has never been through `EnsureOutput`. And the two "is the *other*
sink missing too?" branches collapse into one rule: say something when nothing
reaches the Output at all, and stay quiet when one half is wired and the other
is not, which is a patch built for the eye or for the ear and is deliberate.

`Presets.Empty` is now genuinely the smallest patch there is: the Output, alone,
with everything still to plug into it.
