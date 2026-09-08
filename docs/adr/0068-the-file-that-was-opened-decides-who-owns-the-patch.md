# ADR-0068: The file that was opened decides who owns the patch

**Status:** Accepted · 2026-09-02 · *user-directed* · the code view;
implemented in `Controls/SourceView.cs` and `MainWindow.Source.cs`; rests on
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

**Saving follows ownership too.** A source-owned document written as `.fbks`
writes the text itself — comments, names and `def`s — and is a save like any
other: it takes the name in the title bar and answers the unsaved question. A
graph-owned one prints, and stays a copy. Saving as `.fbk` or as a bundle makes
that the document and hands the patch back to the graph.

## Consequences

**Typing that has not been applied is now something to lose**, and the unsaved
question had to learn about it. Nothing typed reaches the patch until somebody
asks, so the editor's history cannot know a document has moved on.

**Undo, redo and laying out follow the view rather than the owner.** All three
act on what somebody is looking at: on the canvas they are the modules' and on
the text they are the lines'. So Ctrl+Z takes back the last thing typed while
the text is up and the last evaluation while the canvas is, and Ctrl+L folds
the long lines or lays the modules out — which are the same thing done to the
two views of one patch, and are each other's counterpart in the engine besides
(`SourceLayout` and `PatchLayout`).

Neither stack is disturbed by the other, which is what makes switching over
worth doing: a run of evaluations is still there to be undone on the canvas
after an afternoon of typing. The one thing switched off is laying out a
*locked* canvas, where the binder re-lays it on the next evaluation and a tidy
would not survive one.

**Loading a document empties the text's stack, and should.** Opening a `.fbks`
or taking a printing of the canvas is a new document rather than an edit, so
Ctrl+Z cannot reach back into the text of something else that was open earlier.
Folding the lines is an edit and one press takes it back.

**The inspector goes dim rather than away.** A knob turned there would be wiped
by the next evaluation, and a value nobody can read is worse than one nobody can
turn. Turning knobs from a locked canvas — writing the value back into its
`name.port = 0.6` line, which the language already has a statement for — is the
obvious next thing and is not here.

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
