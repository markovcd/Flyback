# ADR-0159: A turn pays only for what is new

**Status:** Accepted · 2026-09-29 · *user-directed* · amends
[0113](0113-the-workbench-does-not-limit-how-large-a-patch-is.md)

## Context

Every request of a turn resends the whole conversation, so anything a tool puts in
it is paid for again on every request after. Several things were put in that the
model already had, or kept that it no longer needed:

- `write_patch` printed the whole patch back, because it renamed every module from
  its type id and the model could not otherwise find `let kick` again. A large
  patch was its source twice over.
- Every edit's reply listed every standing warning again.
- A picture or a clip from an early turn went with every later request.
- A refused handle listed every handle in the patch.

Some of it was worse than waste. The 200-call budget ran for the whole
conversation and refused `propose` with the rest, so a long conversation ran dry
for good. A stop landing between two calls of one batch left calls unanswered,
which makes every later request a 400. A turn that failed before anything came
back (a rate limit, a refused key) still used up a turn.

## Decision

**A module written with `let` answers to that name.** `write_patch` replies with a
count, the handles of modules that had no name, and what the compiler says —
never the patch. `describe_patch` is for the start of a conversation; after that,
each message says what changed on the canvas (ADR-0156).

**A complaint is said once.** After an edit, the compiler's issues and warnings are
said in full when they are new and counted when an earlier reply said them.

**Pictures and clips last a turn.** At the start of each turn, the ones from
earlier turns are replaced with a line saying one was there. The model can
render or listen again. This invalidates the provider's cache from the first
picture once per turn, not on every request.

**The budget is a turn's.** Tool calls and edits count from each turn's start, and
`propose` is never refused for the count, since it is what running out asks for.

**Every call is answered.** Calls a stop cut off, and calls after a `propose` in
the same batch, get a line saying they were not run. A turn that failed before
anything came back is not counted.

**A mistake refuses the whole call.** A knob list with one bad entry sets none, and
a module whose knobs are refused is not placed.

**A warning does not stop `render` or `listen`.** Only an error does, and the
warnings go in the caption.

## Consequences

**A standing error said many turns ago is only counted.** The model has it in its
history; a model that has lost track can call `describe_patch`, which says
everything.

**Two modules written with the same `let` name in two stamped `def`s cannot both
have it.** The first keeps it and the second is named from its type id, and the
reply lists it.
