# ADR-0068: The file that was opened decides who owns the patch

**Status:** Accepted · 2026-09-02 · *user-directed* · the code view;
implemented in `Controls/SourceView.cs`, `MainWindow.Source.cs` and
`Language/SourceMap.cs`; rests on
[0065](0065-a-text-language-that-parses-to-a-patch.md) and
[0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md); does
not disturb [0004](0004-visual-patch-editor-as-the-authoring-model.md)

## Context

[0065](0065-a-text-language-that-parses-to-a-patch.md) gave the patch a second
way to be written, and [0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)
made rebuilding one cheap enough to do while it plays. What was left was a
window with one patch in it and two ways to edit it — and no answer to the only
question that matters: which of them is the patch.

The two directions are not symmetrical, and that asymmetry is the whole design
problem rather than a detail of it:

- **Building text into a patch is exact.** Text in, patch out, deterministic,
  and now with stable identities besides.
- **Printing a patch back out is lossy.** `docs/language.md` lists it: node ids
  are regenerated, canvas positions are re-laid, and **groups go entirely —
  name, membership and all**. That last is documented as "worth fixing and it is
  not fixed", and the reason is structural: the printer writes a binding where
  it is first needed, and a group's members are not contiguous in that order.
  Comments, formatting and every `def` go too, since a patch holds none of them.

Two shapes were considered and both were wrong.

**A mode that owns the patch** — whichever view is showing is the one being
edited — states simply and hands somebody a trap. Switching a graph to the text
view has to print, and printing silently discards the groups they drew. They
would be warned every time, or never, and neither is good.

**Text as the primary document** — `.fbks` is the file, the graph is a view —
is cleaner to explain and is the wrong size of decision to make here. It
reverses [0004](0004-visual-patch-editor-as-the-authoring-model.md); it makes
every canvas gesture a text edit, which is a much larger piece of work; and it
runs straight into the printer's group gap as a blocker rather than a wart,
because a group drawn on the canvas would have to be writable back into text.

## Decision

**The file that was opened is the document.** Open a `.fbks` and the text owns
the patch. Open a `.fbk`, a bundle, or pick a preset and the graph owns it. The
text view opens either way; what it shows differs.

**A printing is offered, labelled, and adopted only on purpose.** Opening the
text view over a graph-owned patch prints one and says so above it: where it
came from, how many groups it left behind, and that applying it makes the text
the document instead. Nothing changes hands until somebody presses Apply, which
is how a patch is deliberately taken into text.

**A printing never overwrites typing.** It is made only over a buffer nobody has
touched. Somebody who typed, went to the canvas to check a wire and came back
would otherwise find their work replaced by a printing of a patch they had not
changed, which is the worst thing this feature could do.

**Two views of one row, never both.** The text sits where the canvas sits and
they swap, rather than the text being a third panel stacked under the assistant.
Both showing would halve the room for each and put the ownership question back
on the screen. `F2` and a toolbar toggle switch them.

**Ctrl+Enter builds the whole buffer.** Not the block under the cursor: the
language builds a whole patch and there is no interpreter under it
([0065](0065-a-text-language-that-parses-to-a-patch.md)), so block evaluation
has no meaning here to borrow. It is caught on the way *down* — a TextBox that
takes newlines handles Enter in its own class handler and marks it dealt with,
so a handler added the ordinary way is never reached.

**Applying is an edit, not a new document.** One press of Ctrl+Z takes an
evaluation back, so the canvas history becomes a history of evaluations.
Nothing is rewound: the point of applying a patch while it plays is that it goes
on playing, and everything the edit did not touch keeps its accumulator and its
delay line ([0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md)).

**A text that does not read changes nothing at all.** The complaints go against
the lines they are about and clicking one puts the caret there. Whatever was
playing goes on playing — there is no half-applied state to be left in, because
the language builds a patch or refuses to, and that is what makes an evaluation
safe to *try* rather than something to be sure about first.

**A source-owned canvas keeps everything that looks and loses everything that
changes.** `NodeEditor.Locked` takes away wire drags, node drags, delete, cut,
paste, group and the module list, and leaves selecting, panning, zooming,
framing and copy. It is still how somebody reads a patch and picks the module
the inspector is about. The gates are on the gestures rather than on the methods
behind them: those are the shell's to call, and a public method that silently
did nothing would be worse to hand a caller than a button that is visibly off.

**The panel says what the canvas can actually do.** The empty inspector lists
every gesture, and half of them have just been switched off — so a locked canvas
gets its own list. Naming gestures that do not work would have somebody follow
them and conclude the program was broken rather than that the patch belongs to
the text.

**The caret points the panel, by position and not by name.** A module is what
the words under the caret are: the innermost call containing it, or — where the
caret is on a binding's own name — the module that binding names, which is the
last stage of its pipeline. So clicking about in the code selects on the canvas
and fills the inspector, exactly as clicking a module does.

By position because the text people write does not name everything in it. The
printer folds a module used once into the pipeline that uses it, so
`atan2(a: 1.5) |> out.left` is a whole patch in which no module is called
anything. Pointing by name would mean giving every module a binding first —
rewriting somebody's file, or making printings verbose, to answer a question
about where the caret is. What the two ends record instead is where each module
was written: `Binder` notes it as it builds, `PatchPrinter` as it writes, and
`SourceMap` turns a line and a column into an offset and a span by reading the
text again.

**The inspector stays live on a locked canvas, and knobs are written back where
the code already says them.** A knob turned there is heard at once — that is the
point of turning one while a patch plays — and on the way up from the gesture the
number in the file is changed in place: `atan2(a: 1.5524476)` becomes
`atan2(a: 2)`. A knob sitting at its default is written nowhere, so it is added
to the call that placed the module rather than said again lower down, which would
leave the file asserting two values for one socket with the older one still
written above. The Output is the exception and takes `out.left = 0.6`, because
nothing places it.

On the release rather than on every frame: a drag is one gesture and should be
one edit, not a hundred things to undo and a line flickering under whoever is
reading it. Nothing is rebuilt — the patch already has the value and the engine
already has the patch, and building here would replace the patch under the very
control being dragged.

**Everything a module carries goes back the same way, not only its knobs.** A
tune and a scale are written into the block after the call, a file into the one
string a call carries without a name, and a plugin's declared field into a named
argument beside the knobs — each where the language already says it, and each
added where the text does not say it yet. An emptied tune is written as `[ ]`
rather than by taking the block away, for the reason a knob dragged back to its
default is written rather than deleted: what the panel says and what the file
says have to be the same thing, and silence is not a value.

This is what closed the last hole in the language. A plugin's field could be
read from a `.fbks` and was never written to one, so printing a patch dropped
it — and a Fractal's octave count is not decoration, it decides how many octaves
the program builds. A patch that could not be written as text without becoming a
different instrument is a patch the text view could only ever half-show.

**What is still the text's is what the graph is made of.** The buttons that
group, ungroup and delete are not offered on a locked canvas, and the title does
not rename. Those change which modules exist and what they are called, and the
next apply builds that from the file — so a button offering them would be
offering something the file takes straight back. Where a value cannot be written
either — a module stamped out of a `def` more than once, or one written as
`1 / 12` — it is said in the status bar rather than dropped quietly.

**Saving follows ownership too.** A source-owned document written as `.fbks`
writes the text itself — comments, names and `def`s — and is a save like any
other: it takes the name in the title bar and answers the unsaved question. A
graph-owned one prints, and stays a copy. Saving as `.fbk` or as a bundle makes
that the document and hands the patch back to the graph.

## Consequences

**Typing that has not been applied is now something to lose**, and the unsaved
question had to learn about it. Nothing typed reaches the patch until somebody
asks, so the editor's history cannot know a document has moved on.

**Laying out follows the view.** Ctrl+L folds the long lines or lays the modules
out — the same thing done to the two views of one patch, and each other's
counterpart in the engine besides (`SourceLayout` and `PatchLayout`). The one
place it is switched off is a *locked* canvas, where the binder re-lays it on
the next evaluation and a tidy would not survive one.

**Undo and redo follow the owner, and only then the view.** They began as the
view's, which was wrong in the way that only shows up once somebody works in the
text for a while: typing went on one stack and evaluations on another, neither
knew when the other had happened, and Ctrl+Z after an apply took back a line
typed some minutes earlier while leaving the patch it had already been built
into exactly where it was.

So where the text is the document, its stack *is* the document's history, from
either view. Typing, applying and turning a knob are one run of things somebody
did and come back in that order, because the last two go on that same stack
rather than on a second one that would afterwards have to be interleaved with it
by guessing. The canvas is a view of that document, so Ctrl+Z there means what
it means at the text.

Where the graph is the document the two are independent again and undo follows
the view: the modules' steps on the canvas, and whatever has been typed into a
printing at the text. Either way the gesture falls through when the stack it
lands on has nothing left — a printing is loaded rather than typed, so applying
one would otherwise be the single thing nobody could take back.

**What goes on the text's stack for a patch step is a count, not a copy.** The
canvas is already keeping the snapshots and keeping them twice is how two
records of one edit come to disagree, so what is pushed is how many of its steps
to walk back. An undo at the text is an undo at the canvas: one history, reached
through whichever view somebody is working in. The count is exact because the
canvas says when it actually made a step — an edit that changed nothing makes
none, and a hundred frames of a knob being dragged make one.

**A knob turned in the panel is one thing done, however little of it is text.**
The number is written into the code on the release and the value reached the
patch on the way there, and both come back in one press: they are grouped, so
taking back the text without taking back what it does — the file reading
`1.5524476` over a patch still playing `2` — is not a state the program can be
left in.

**Applying is a handover as well as an edit, so taking it back is both.** An
evaluation puts a patch on the canvas *and* makes the text the document, and
undoing only the first half leaves a canvas nobody wrote any text for locked
behind text claiming to describe it — with a handover made by hand as the only
way out, which is not what Ctrl+Z was pressed for. So who owned the patch is
noted beside the step the evaluation records, an undo across that step gives the
canvas the patch back, and a redo takes it into text again.

The view goes with it, because the handover is the half of that edit somebody
can see. Taking an adoption back puts the canvas up — that is where the modules
they wanted back are, and it is what the same handover made by hand already does
— and putting it back shows the text that is the document again. Pressing undo
on the canvas moves nothing, since that is where it lands.

The note is opaque to the history, which knows only that it rides with the step.
A snapshot says what a patch was and nothing about where it came from, and the
canvas records a step for every gesture it has rather than knowing what else
each one changes. A handover made by hand records nothing, because nothing about
the patch changes to make it — so the steps behind it are re-marked to whoever
holds the patch now, and undoing a drag does not take back a handover no edit
was made for.

**Loading a document empties the text's stack, and should.** Opening a `.fbks`
or taking a printing of the canvas is a new document rather than an edit, so
Ctrl+Z cannot reach back into the text of something else that was open earlier.
Folding the lines is an edit and one press takes it back.

**A printing is now something to click as well as something to read**, and it
did not have to be renamed to become one. What the printer hands back beside the
text is the modules whose calls stand in it, in the order it wrote them; the
text is read back, and the calls a parser finds line up against that list. So
nothing counts characters — folding the long lines may move whatever it likes —
and a printing a knob has been written into is mapped again from the same list
rather than printed afresh, which would replace what somebody is reading in order
to say a thing the text already says.

**What a write-back puts in a printing is not an edit to take back.** It is the
reading keeping up, and nobody made it — so it comes off that stack, and Ctrl+Z
over a printing falls through to the canvas, where the only history of a
graph-owned patch is. Left on the stack it would answer the press by putting the
old number back over a patch still playing the new one, and by leaving the text
no longer the printing it claims to be: a text that is not the printing maps to
nothing, so the caret would stop pointing the panel. The same words in a
*source-owned* document are an edit and stay one — there the text is what the
patch is built from, and taking a number back there is taking the patch back
with it.

**A printing keeps up with an undo as it keeps up with a knob.**
 An undo is the
one thing that moves a graph-owned patch while the text is showing, the canvas
not being on screen to be dragged, and a reading that went on saying the old
number over a patch that had gone back to the old one would be a reading of
nothing. So the printing is made afresh whenever the patch comes back out of the
canvas's history — and only over a buffer that is still the printing, since
typing is never printed over.

**A printing stops being mapped the moment somebody types into it.**
 It is then
text about a patch that may no longer be there, and the honest answer is to point
at nothing rather than at whatever used to be under the caret. The guard is that
the number of calls still matches.

**Nothing about `.fbk` changed.** Presets, bundles and every patch anybody has
open behave exactly as before, which is the point of settling this by provenance
rather than by format. This is a strict subset of making text primary: if that
turns out to be where this should end up, what is here is widened rather than
undone.

**The shell takes a package, which it had not done before.** A `TextBox` has no
rich text in it at all, so a gutter would mean a second control scrolled in step
with a scroll viewer reached for through the template, and highlighting would
mean nothing. Avalonia's own `RichTextEditor` is the wrong shape — a word
processor with RTF and DOCX serialisation, no syntax highlighting, no line
numbers, and a Pro licence besides. So the editor is
[AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit), which the Avalonia
organisation ports and publishes under the same licence this program carries,
version-matched to the Avalonia already here.

ADR-0019's rule is the *engine's* and `Flyback.Core` still answers to it; this
is the shell, which already carries Avalonia, Skia and a font. What it buys: a
gutter with line numbers, the complained-about lines washed in the Output's own
red, the current line marked, and the language coloured.

**The colours are written by hand, against `docs/language.md`.** No grammar
registry has heard of this language, so TextMate would have meant a second
package and a definition to write anyway. `Flyback.xshd` sits beside the control
and uses the shell's own palette, so a socket name in the text is the pale green
a socket carries on a node and the Output is the sink's red. A call is coloured
by the bracket after it rather than by looking the name up, because a plugin's
modules are usable the moment it loads and a list here would be one more thing
that could go stale — the same reason the binder reads the catalogue rather than
a table.

## Amendments

**2026-09-08 — the canvas can be given the patch back.** Applying was the only
door in the wall. It is described above as the deliberate way a patch is taken
into text, and it is; what was missing is that there was no gesture at all for
the other direction. Once a text had been applied, the canvas stayed a view of
it until some *other* document arrived — so somebody who applied a printing to
try something, and then wanted to drag one wire, had to save the patch as a
`.fbk` to be allowed to. A one-way gesture is a trap however well it is
labelled, and the way out of an adoption should not be a file operation.

So **Edit on the canvas** sits beside Apply, under the text, and is shown only
while the text is the document — over a printing the canvas has the patch
already and the button would do nothing. What it does is exactly what a `.fbk`
arriving does: the graph owns the patch, the canvas unlocks, and the buffer is
emptied. It goes through the same `DropSource` as opening a patch file rather
than through a path of its own, because it is the same change and a second way
of making it would be a second way of getting it wrong.

**Nothing is built and nothing is rewound.** The patch on the canvas is already
what the text made, so handing it over is a change of owner and not of program:
what is playing goes on playing, which is the same promise applying makes.

**The text is asked about first, and the patch is not.** The buffer is emptied
by the handover and written nowhere on the way, so typing that is not on disk
yet gets the same three answers a close gets — Save…, Discard changes, Cancel —
under its own heading, since what is at risk is the text rather than the patch.
Text already written out as `.fbks` is on disk and is not asked about. The patch
is deliberately left out of the question: it is not going anywhere, and it stays
as unsaved as it was a moment before, so the question the file asks is still
there to be asked when a file asks it.

**The next look prints afresh.** An emptied buffer is what
`PrintForReading` waits for, so the text view opened again shows a printing of
the patch under the notice that says so — the state somebody would have been in
had they opened the patch as a `.fbk` all along, which is the point.

**2026-09-08 (later) — the view is where somebody is, and a document arriving
does not move them.** Handing the patch back is a change of owner, and so is
picking a preset, opening a `.fbk` or saving as one: all four go through the
same `DropSource`, which is right. What was wrong is that it also put the canvas
back on the screen, so a preset picked from the text view answered a question
nobody had asked — where to look — and every patch tried from there landed on
the canvas.

So the handover leaves the view alone and prints into whichever one is showing.
A preset picked while the text is up is read as text, in the view it was picked
from, and what changes is that the text is now a printing and says so above
itself. Printed straight away rather than on the next look, because this *is*
the look: an emptied buffer left in front of somebody is a text view claiming
the new patch is nothing at all.

Two documents still move the view, and both for the same reason — the view they
would leave somebody in is not one they can work in. A `.fbks` arrives as text
and locks the canvas, so it opens the text view. **Edit on the canvas** shows
the canvas, because wanting to draw there is the whole of what pressing it
means; it asks for the view itself rather than getting one as a side effect of
the handover, which is what the other three callers wanted all along.

**2026-09-08 (later still) — a preset picked from the text view is read into
text.** The view no longer moving left the other half of the answer wrong. A
preset picked while the text was up arrived as a printing over a canvas that
still owned it, so somebody working in text was looking at a reading and could
still drag and delete modules behind it — the question this record exists to
settle, put back on the screen.

So the printing is made and applied at once: the two steps somebody would
otherwise take by hand, done for them, because picking a preset *from the text
view* has already said which of the two they mean to work in. It goes through
`Evaluate` rather than by setting a flag, since the text can only be the
document if the patch on the canvas is the one the text builds — ids and all —
and a printing adopted without being built would point the caret and the panel
at modules that are not there.

**Only a preset, and what decides that is what printing loses.** A preset holds
no groups — none of the built-in ones and none a plugin offers — so its printing
is the same instrument written another way, and adopting it costs a layout that
the next apply would redo anyway. A `.fbk` or a bundle is somebody's own patch
and its groups are their work, so those stay the graph's and their printing
stays offered rather than taken. That is the rule above narrowed rather than
dropped: a printing is still never adopted where adopting it would lose
something.

**It arrives with nothing to lose.** Nothing has been typed and nothing drawn,
so the patch and the text are both marked as they stand — the same state a
preset picked on the canvas leaves. A title claiming unsaved work a moment after
a preset was picked would be claiming somebody else's.
