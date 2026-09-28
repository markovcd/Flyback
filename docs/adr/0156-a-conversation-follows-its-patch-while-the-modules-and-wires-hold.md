# ADR-0156: A conversation follows its patch while the modules and wires hold

**Status:** Accepted · 2026-09-28 · *user-directed* · amends
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) and
[0072](0072-a-conversation-is-saved-with-the-patch-it-is-about.md)

## Context

Whether the patch on the canvas was still the one a conversation was about was
decided by object identity and two counts. That was wrong both ways. An undo hands
back a new copy of the same patch, so undoing a knob started a new conversation,
paying for the whole briefing again and losing the history. A knob turned in place
kept the same object, so the conversation carried on — but the workbench never saw
the knob, and the next proposal put the old value back without saying so. A knob
turned while a turn ran was lost the same way.

## Decision

**A patch is the same patch while its modules and wires are.** The same module ids
of the same types, and the same wires. Anything else — knobs, names, whether a
module is off, what a module holds besides its knobs, the panel's knobs, the
description — is a setting. Adding or removing a module or a wire is still a new
conversation, as before, for the conversation and for what is saved with the file.

**Settings changed on the canvas are carried in, and the model is told in a line.**
Before each message, whatever changed since the workbench last looked is set on the
workbench, and the message goes out behind one bracketed line naming each setting
by the handles the model uses: `value1.value=0.25, knob "Cutoff"=0.7`. That is a few
dozen tokens where a new conversation was the whole briefing, and the provider's
cache of the history stays good. A conversation carried on from a file takes in
whatever the file was saved with since.

**A proposal is merged with what was turned while it was made.** Three ways, by
module id: a setting changed on the canvas during the turn goes into the proposal
unless the assistant changed the same one, in which case the assistant's stands —
it was asked to change something. Modules and wires are not merged: a proposal over
a patch whose shape moved still replaces it, and says so, and Ctrl+Z has it back.

## Consequences

**A proposal is no longer quite all-or-nothing.** ADR-0033's proposal was a whole
patch taken or not; it is now a whole patch with the person's settings laid over it.

**A text document is followed only until a proposal lands in it.** Applying one to
a document the text owns builds its modules afresh with ids of the text's own, so
nothing turned there afterwards matches the workbench, and it goes unmentioned
until the next conversation.

**The kept conversation of a `.fbk` still needs the file's exact text.** A knob
changed by another program is a different file (ADR-0072); only edits made in the
editor are followed.
