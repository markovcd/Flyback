# Propose refactors

## Say so when the code has outgrown its shape

When the code a task works in has gone untidy, ballooned out of proportion to what it does, or would plainly read better shaped another way, propose the refactor. Do not wait to be asked, and do not quietly work around it.

**Why:** the user asked for it outright. Code that has drifted gets worse one careful patch at a time, and the session in it is the one that sees it most clearly.

**How to apply:**

- Signs: a method or class that has doubled while doing one thing, a flag threaded through four layers, the same few lines in three places, a file that needs scrolling to find the part being changed, a change that had to touch far more than it should.
- Check `docs/adr/` first (the `adrs` skill): a shape an ADR chose, or a refactor one declined, is not drift.
- Finish the task in the code as it stands, then end the reply with the proposal: what hurts, in a line, and the shape it should take. Name the files.
- It is a proposal. Do not refactor unasked, and do not fold it into the task's commit. When the user agrees, do it as its own commit, or add it to `TODO.md` if it is for later.
- Say it once. A proposal the user declined, or one already in `TODO.md`, is not raised again.
