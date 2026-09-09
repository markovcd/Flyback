# ADR-0071: Two undo stacks, and which one a press lands on

**Status:** Accepted · 2026-09-09 · records a mechanism that was already built,
across `Graph/PatchHistory.cs`, `Controls/SourceView.cs`,
`MainWindow.Source.cs` and `MainWindow.Editing.cs`; the half of
[0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md) that says
what ownership costs

## Context

There are two undo stacks, and there have to be.

The canvas keeps `PatchHistory`: JSON snapshots of the whole patch, two hundred
deep, where a step is the document as it stood and undoing is loading one. The
text keeps AvaloniaEdit's `UndoStack`, which is the editor's own and knows about
characters. Neither can be the other. A snapshot history cannot express "put back
the word you deleted" without printing and reparsing the patch on every
keystroke, and a character history cannot express "put that module back" at all.

[0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md) settled who
owns the patch, and left this: while the text owns the document, typing, applying
and turning a knob are one run of things somebody did, in the order they did
them. A person who types, applies, drags a slider and presses Ctrl+Z four times
expects those four things back in that order. Two stacks cannot give that by
being asked in turn — interleaving them afterwards would mean guessing which of
two undo steps happened first, and neither stack records when.

What made this worth writing down is not that it is complicated. It is that the
rules were nowhere. They lived in four flags, two sets and six methods spread
across three files, every one of them commented and none of them saying what the
others were for, and the thing actually holding the mechanism in place was a
1 500-line test file. A protocol whose only specification is its test suite is
one that cannot be reasoned about before it is changed, only after it breaks.

## Decision

**Record the protocol; do not restructure it.** Five rules, and this is where
they are written.

**1. A press lands on one stack, and one expression says which.** The text's
stack where the text is the document's history and has something left, the
canvas's otherwise, and neither where neither has anything. `UndoLandsOn` is that
expression, and the toolbar greys its button from the same property the gesture
acts on — so a button offering a press that does nothing, and a press doing
something the button said it could not, are the same impossible bug rather than
two likely ones.

Which history is the document's follows the *view*, not the owner: looking at the
text is what makes Ctrl+Z mean the last thing typed. Switching views hands them
back, and neither stack disturbs the other.

**2. While the text owns the document, a canvas step goes onto the text's
stack.** Not as a copy of the patch — as a `Deed`, an `IUndoableOperation`
holding a count of how many canvas steps to walk. The canvas is already keeping
those steps, and keeping them twice is how two records of one edit come to
disagree. One history, reached through whichever view somebody is working in.

**3. The count waits for the write-back.** A knob turned in the panel is one
edit on the canvas and one write into the text, and they are one thing somebody
did. `unstacked` counts canvas steps not yet handed over; the write-back hands
them across as a single `Deed`, so one press takes back the number and the sound
together.

**4. A press that falls through to the canvas pays the count back.** A step
reached that way was one the text's stack had not been told about — or the press
would have landed there instead. Left counted, the next write-back would hand it
over as well, and one press there would take back two edits, the second of them
one somebody had already taken back by hand. `Owed(±1)` is that repayment.

**5. Nothing goes onto a stack while that stack is being read off.** Walking a
step rebuilds the panel, and a control losing focus to that looks exactly like a
write-back to everything downstream. `stepping` and `writingBack` are the two
guards, and they are guards rather than queues because the work they are keeping
out is work that has just been done.

Ownership rides along in `PatchHistory`'s `mark`, which the canvas takes
opaquely: an evaluation is the one edit that changes hands, so undoing one has to
put the hands back too, and `Handed` is where that happens as each step arrives.

## Consequences

**The rule that two things both needed is now one thing.** It was stated twice —
once as the gesture and once as the button's enabled state, with a comment on the
second saying it was "the same answer the gesture itself gives". It was, and
saying so in a comment is what you write when the compiler cannot.

**A count is a bridge, not a proof.** `unstacked` is an integer maintained by
hand across an event, two guards and a repayment, and nothing checks it against
what either stack actually holds. If it is ever wrong the symptom is a press
taking back the wrong number of edits, which is both the worst kind of bug this
program can have and the kind no type here would have caught — the count is
correct by argument, and the argument is above.

**The test file is still the specification, and now it is not the only one.**
`SourceViewTests.cs` is 1 500 lines because every rule above is only observable
by driving both views and watching what comes back. That is the right size for
what it covers. What changes is that a reader can now find out what it is meant
to be proving without reverse-engineering it from the assertions.

**The coupling stays.** Everything here reaches the canvas, the text view, the
panel and the toolbar within one gesture, which is exactly the case
[0039](0039-one-window-class-across-a-file-per-region.md) examined and declined
to extract: a collaborator class would need four or five of the window's fields
passed in and would hand most of them straight back. This record is the
alternative to that extraction, not a step towards it.

**What was considered.** *One stack, keeping commands rather than snapshots* —
rejected upstream by `PatchHistory` itself, and it would not help: the text's
stack would still be AvaloniaEdit's. *Printing the patch on every keystroke so
one character history covers both* — a reparse per keypress, and a patch whose
groups do not survive printing
([0065](0065-a-text-language-that-parses-to-a-patch.md)), so undo would quietly
lose them. *Asking both stacks and interleaving by timestamp* — the guess this
mechanism exists to avoid.
