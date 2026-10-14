# ADR-0196: A tutorial step is a patch the editor checks against

**Status:** Accepted · 2026-10-14 · builds on [0065](0065-a-text-language-that-parses-to-a-patch.md) and [0087](0087-the-assistant-moves-to-a-column-beside-the-patch.md)

## Context

The tutorials were videos on the site. Nothing in the editor taught anything, so
a newcomer watched in one window and patched in another, with no way to know
whether what they built was what the video built. The user asked for tutorials;
the form was left open.

## Decision

**A tutorial lives in the editor, in the column left of the canvas.** It shares
that column with the assistant: opening one puts the other away. It opens from
Tutorials at the left of the status bar, in every shell, the web editor included;
the toolbar has no room left for another button at 830 pixels.

**A tutorial is a file.** `src/Flyback.Engine/Tutorials/<id>.md`, embedded:
a `# title`, a `From:` line naming the preset it opens on, a summary, then a
`## heading` per step with its text and at most one ```` ```fbks ```` block, the
step's goal. A step without a goal is read and moved past by hand.

**A goal is written in the text language, and checking it is matching it.**
`StepCheck` builds the goal and looks for it inside the patch: a module of each
kind it places, wired as it wires them, with each knob it moves off its default
within two percent of its value. Everything else on the canvas is the person's
own and never held against them. A step not met says the first thing missing in
words: "Add a Sine.", "Wire the Sine's out into the Output's color.", "Turn the
Sine's freq to 4; it is at 1." A goal needs no matcher of its own and reads as
what the step builds.

**The column checks after every change to the patch** (`PatchChanged`), ticks
off the step and any after it the patch already meets, and stays on a step gone
back to until the patch next changes.

**`flyback-cli tutorial` is the same check without a window**: it lists the
tutorials, says one's steps, and checks a patch against each step, exiting 1
when one is unmet.

## Consequences

- A tutorial is checked by tests: every goal builds and meets itself, and no
  step is met by the goal before it, so nothing is ticked off before it is done.
- A goal knob left at its default is not checked; a step that wants a default
  says so in its text.
- An Expression's formula is not compared, so goals keep to modules.
- Progress is not saved: closing the column or the editor starts a tutorial over.
