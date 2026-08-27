# ADR-0046: The module list is a gesture, not a panel

**Status:** Accepted · 2026-08-19 · *user-directed* · takes a column back from
[0039](0039-one-window-class-across-a-file-per-region.md)'s layout

## Context

The shell was four regions across: a module list, the canvas, a splitter, and the
inspector. The list was 220 pixels wide with a minimum of 150, and it stood open
whether or not anything was being added — which is almost always. It is read when
a module is wanted and ignored the rest of the time, and the rest of the time is
most of the session.

That width is worth more to the canvas than to a list, and the canvas is the
thing the program is for.

## Decision

**Right-clicking empty canvas opens the list at the pointer.** A flyout, not a
window: it dismisses on a click elsewhere, it needs no chrome, and nothing about
it has to be arranged around.

**What is picked lands where the click was.** This is most of the point. The list
used to drop a module in the middle of the view, because a button down the left
of the window has no place in mind; a right-click does, and it is the place the
hand is already at. `NodeEditor.AddNode` takes an optional point and keeps the
middle of the view for callers that have nothing better to say.

**The list itself learned nothing about how it is shown.** `ModulePalette` is a
control that takes a catalogue and a callback. It was a partial of `MainWindow`
because it was a region of the window; it stopped being one, so it stopped being
that.

**The right button gave up panning to take this on.** It used to do both, which
would have meant telling a click from a drag on the way up — the shape the
deferred collapse already needs elsewhere. Panning is the middle button and
nothing else now, and with nothing to be told apart from, the list opens on the
press rather than the release.

**Not over a module.** A right-click there is about that module, whatever the
program may come to do with it, and it is not about adding another one beside it.

**Space opens it as well, at the pointer.** So the hand need not leave the
keyboard, and what arrives still lands where the eye is — the canvas remembers
where the pointer last was, and falls back to the middle of the view for a
pointer that has never been over it.

**It is worked entirely from the keyboard.** The filter takes the focus on
opening and keeps it the whole time: the arrows move a highlight through the
list without ever taking the focus off the box, so typing goes on narrowing
while they move. Enter adds what the highlight is on, and the highlight starts on
the first match — so typing a few letters and pressing Enter is the whole
gesture, with no arrow key in it.

**Enter confirms and Space does not.** Space is a character and the box it
belongs in is right there. A list where the obvious confirm key also typed into
the filter would be one or the other at random.

**Escape empties the filter, and empties nothing a second time** — so the same
key then closes the list. One key, and it always takes a step back.

**A wire let go over bare canvas opens it too, and what is picked arrives
plugged in.** The same list, unfiltered: there is nothing to filter by. The
compiler broadcasts a scalar to three channels and takes luma from a color, and
`CompleteWire` enforces no rule of its own, so every socket in the catalogue
accepts every wire. Only one module of sixty could be excluded even by
direction — Coordinates, which has no inputs — and it is not worth a rule.

**Which socket it lands on is the real question, and it has three answers in
order.** First a port marked `Domain` or `Swept`: those name the axis a module
is read across and the signal it reads under a domain of its own, which is to
say the socket the module exists to have something in. The compiler already
agrees — it warns about a Domain port left on its knob and about no other. Then
an exact match of kind, which is what tells a Scan's `view` from its `out` when
a color was wanted, and puts a stray scalar into a Blend's `t` rather than
broadcasting it to grey down `a`. Then the first socket, which is where this
would land anyway: the catalogue is written with the principal port first, and
today the two rules above agree with it everywhere. They are there for the
almost.

## Consequences

**The canvas gained 225 pixels and the layout lost two tracks.** Which shifted
every column index in the grid. The fullscreen preview reads the column it must
keep off the layout rather than from a constant
([0039](0039-one-window-class-across-a-file-per-region.md)), so it needed no
change — but its test found that grid by counting columns, and three columns no
longer tells it apart from the toolbar's three. The grid is named now, which is
what it should have been.

**Nothing on screen says how to add a module any more.** A panel that is always
there is its own documentation and a gesture is not. The inspector's
no-selection text says it first, before anything else, along with what the
keyboard does inside it — and that text is now the only place several of this
shell's gestures are written down at all.

**Panning lost a button.** The right one did it as well as the middle one, and
a right-drag now does nothing at all. That is a gesture taken away rather than
moved, and it is the price of the right button meaning one thing.

**The highlight is state on the palette rather than the focus.** Focusing each
module as the arrows reach it would be the ordinary way to do this, and it
cannot be: the filter has to keep the focus so that typing keeps narrowing. So
the palette tracks an index into the buttons it listed, paints that one, and
raises its Click on Enter — which is also why the arrows walk that list rather
than the visual tree, where the category headings sit between the modules and
would be stepped onto as though they were choices.

**The ticks per plugin survive the list being closed.** One palette, built once
and kept, because which plugins are showing is a setting rather than something to
be re-answered every time the list opens. The filter does not survive, for the
opposite reason: it is where you got to last time and never what you want next.

**A flyout is not in the window's own render.** The tests find the control and
measure it, which is what catches a popup that opens with no size — but a
screenshot of the window does not include it, so what it looks like is checked by
a person rather than by a test.

**Amended: the list holds kept groups as well as the catalogue.** A box saved
from the group inspector is listed above the modules under `GROUPS`, and picking
one adds a fragment rather than a module — the modules, their wires, their knobs
and the box round them, landing where the list was opened exactly as a module
does. Which is a second kind of thing in a list built out of a catalogue, so it
is worth saying what keeps it honest: they are not modules and are not pretended
to be. They have no type id, no category and no plugin, they are filtered by name
alone, and the section takes no accent colour because a group has no category to
take one from.

**They are patch files in a folder, and there is no library.** One `.fbk` per
kept group in `groups` beside the settings, each the same fragment the clipboard
carries ([0045](0045-what-is-copied-is-a-patch-file.md)) — so there is no format
to invent, no index to keep in step with the disk, and a patch dropped into the
folder by hand is simply on the list. The name shown is the one on the box inside
rather than the file's, because a name is a title and a file name has been
through a sieve to get there.

**A kept group can be removed from the list, and the row asks first.** The `✕` is
the one the group inspector puts on a socket that can come off the edge, and the
row turns into its own question rather than putting a dialog over the window: the
list is a popup already, and a modal over the shell to ask about one line of it
would take the popup down on the way, since a flyout closes when something else
takes the focus. Asked at all because the entry is a file — nothing out here has
an undo to put one back with.

**Amended: replacing a kept group asks in the panel, the way removing one asks
in the list.** Saving under a name already kept still replaces — that is what
saving under a name it already has means — but not on the strength of one press:
the row it would replace is a file, and a name typed a second time by accident is
the ordinary way to lose one. So the button turns into `Replace “Voice”? ✔ ✕`
where it stands. In the panel and not over the window, for the reason the list's
own question is in the list: it can lose one entry and not any work, and a sheet
over the shell for that would move the panel under the hand that just pressed the
button.
