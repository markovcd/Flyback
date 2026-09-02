# ADR-0067: A module keeps its name, and its memory, across a rebuild

**Status:** Accepted · 2026-09-02 · *user-directed* · the first step of live
coding; implemented in `Compile/StateOwners.cs`, `Compile/DelayState.cs` and
`Language/Binder.cs`; rests on
[0030](0030-oscillators-accumulate-their-phase.md),
[0027](0027-delay-lines-give-the-audio-path-a-memory.md) and
[0065](0065-a-text-language-that-parses-to-a-patch.md)

## Context

Live coding is editing a patch while it plays. Most of what that needs is
already here and was built for other reasons: `AudioEngine` swaps an immutable
program under the callback with one `Volatile` write
([0018](0018-never-render-frames-on-the-ui-thread.md)), the shell recompiles on
every knob turn, and `PatchLanguage.Build` never throws because the thing
reading it is usually an editor ([0065](0065-a-text-language-that-parses-to-a-patch.md)).

Two things were missing, and both are the same problem said twice: **nothing in
this program could say that a module was the same module as before.**

**A cell of memory is found by its position.** `DelayState` says so plainly —
which buffer or accumulator an op uses is its position among the ops of its
kind, counted as the program runs. That is exact and costs nothing while a
program is fixed. Across a recompile it is not an identity at all: insert one
oscillator upstream and every accumulator after it belongs to a different module
than it did.

The renderer could therefore only ask one question, and `DelayState.Fits` is
it: are the counts and the delay lengths identical? If not, throw the lot away.
So:

- adding or removing **any** oscillator restarted **every** tone in the patch
- changing **one** delay time emptied **every** delay line, and restarted every
  tone as well
- and when the counts happened to match but a module had been inserted upstream,
  the slots reshuffled and an oscillator was handed a stranger's phase

On a knob turn none of that is audible, which is why it stood. On a patch being
edited while it plays it is a click or a restart on essentially every
evaluation.

**A patch rebuilt from its source was a stranger to the one it replaced.**
`Binder` minted a fresh `Guid` for every module on every build, so even
rebuilding *identical* text produced a completely different patch. The
workbench's own tool description already named the cost of this — writing a
patch afresh "gives every module a new identity and loses where they sit on the
canvas" — and it is also what made the first problem unfixable, because there
was nothing stable to key the memory to.

## Decision

**The compiler writes down whose each cell is.** `StateOwners` holds, beside the
counts that were already there, the node that owns each delay line, each
accumulator and each one-evaluation cell. Traces already carried a `TapSpec.Node`
and are matched the same way.

**The emitter is told, because it cannot know.** A module claims a cell from
inside its own emit function, which has no idea which node it is being run for;
the compiler is the only thing that does. So `Emitter.Owner` is set around each
`def.Emit`, and saved and put back rather than merely assigned — a swept input
resolves other modules *during* the reading module's emit, so the emission nests
and the owner has to nest with it.

**The two cells the compiler shares belong to nobody.** `Interval` and
`HasMemory` are emitted once and shared by everything that asks, so they take a
fixed name rather than the first module that happened to want one. Attributed to
its first asker, the interval cell would be dropped the moment that module was
deleted — and an interval that reads as a jump is a filter that opens and an
envelope that finishes in one sample.

**A swap adopts by owner, and only by owner.** `DelayState.Adopt` matches each
cell to the cell in the same position *within its owner*, so a supersaw's seven
accumulators stay in order while everything around them moves. A cell whose
owner is `Guid.Empty` matches nothing, including another such cell: not knowing
who owns a cell is not the same as knowing two cells are the same cell, and
being wrong here plays a module something that was never its own. The fast path
stays — an unchanged program gets the very same object back, so a knob costs no
allocation and no copy.

**A line that changed length keeps its tail.** The samples are copied newest
first into a ring of the new size, so a delay time turned while it is ringing
goes on ringing. Only a change of *rate* gives up, and that happens when the
sound device changes rather than mid-phrase.

**A module built from text is named after the piece of source that made it.**
The binder carries a path — `let bass`, then `reverb~0` for a def stamped out
inside it — and a module's id is that path and its position under it, hashed.
Building the same text twice gives the same patch down to the guids.

**Names, not numbers, wherever a statement has one.** A `let` has a name; a
terminated pipeline has the socket it ends at. Numbering statements instead
would give every module below an inserted line a new identity, and a patch that
restarted from the cursor down on every keystroke is exactly what this exists to
avoid. What is left over — a pipeline ending in nothing nameable, a def stamped
out twice in one statement — takes a number, and there an edit is local to the
line anyway.

**The clock, the coordinates and the Output are named for what they are.** There
is one clock in a patch however many lines reach for it, so moving the first
mention must not make it a different module.

## Consequences

**An edit made while the sound plays costs only the modules it touched.** The
test that pins this is written from the sound rather than from the internals: a
tone plays on the left, an oscillator is added to the right, and the left
channel comes out sample-for-sample as it would have if nothing had been
touched. Both continuity tests fail without the change.

**Swapping one module for another keeps its place, and that is a feature.**
`let hum = t |> sine(...)` changed to `saw` is the same name in the same
statement, so the saw inherits the sine's accumulator. That is what a player
wants from changing a waveform on a note that is already sounding — the same
note in a different colour, rather than a click.

**A name is a place in the source, so an edit is local to a line but not free
within one.** Inserting a stage mid-pipeline renames what comes after it in that
statement. Everything in every other statement is untouched.

**The adoption is read on the recompiling thread while the callback may still be
writing the old memory, and deliberately.** Nothing is resized and every cell is
a single aligned write, so the worst of that race is a handful of samples of a
tail arriving out of order — against the alternative, which is the silence this
removes. It is the same trade `DelayState.Tap` and `CopyTrace` already make.

**A program assembled by hand claims nothing and adopts nothing.** Tests and
tools that write ops directly get exactly what they always got, which is memory
starting from silence.

**`write_patch` improved without being touched.** Two rewrites of similar source
now keep the identity of everything the rewrite did not change, so a Meter's
reading, a Scope's buffer and a played note survive it. What it still cannot
keep is a patch that was *not* written in the language: a graph built on the
canvas has ids this has never seen, and rewriting over one replaces them.

**The ids are now a public fact about the language.** A `.fbks` file determines
the guids in the `.fbk` it builds, which is a promise this did not make before —
changing how a module is named would change every patch built from text. That is
a cost worth naming: it is the same class of promise
[0020](0020-json-patch-files-keyed-by-string-type-ids.md) made about type ids.

**What this does not do:** nothing in the shell reads the new identity yet. The
canvas still lays a rebuilt patch out from scratch rather than keeping the
positions of modules it recognises, and selection is not preserved across a
rebuild. Both are now possible and neither was before.
