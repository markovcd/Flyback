# ADR-0136: A letter goes to the author through the preset site

**Status:** Accepted · 2026-09-22 · *user-directed* · uses the site from
[0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md) and sits
beside the counting in
[0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md)

## Context

Nothing in Flyback lets a person say anything to the person who wrote it. The
counting that exists is anonymous by construction and carries no prose, so it
can show that recording is used and never that it is maddening. The instruction
was a button, out of the way, that sends a comment — good or bad — with what
build it came from.

Three things were already here. The preset site takes writes from strangers,
rate-limits them per address and has an admin page where reports are read and
dismissed. The About window already knows the build. And ADR-0094's line —
a fact about Flyback may be gathered, a fact about the person may not — is the
line this has to be on the right side of.

## Decision

**It is called a letter.** `Feedback` is a module, a category and an opcode in
this repository, and the glossary allows one word per thing.

**Letters go to the preset site,** `POST /api/v1/letters`, into the same SQLite
file beside the presets, the reports and the ratings. The admin page lists them
newest first with a Dismiss, as it lists reports. No new service, no key in the
binary, nothing a desktop build has to hold a secret for. `Presets__Letters
PerHour` caps them at five an hour per address, lower than the twenty a
submission gets, because there is no honest reason to write six.

**A letter is a mood, a message, and an address the person may leave blank.**
The four moods — something works well, something is wrong, something I would
like, something else — are there so a hundred letters can be read in an order.
An address is the only way a letter is ever answered, and it is the person's to
give; leaving it blank is not discouraged.

**What the program adds is printed in the letter before it is sent:** the
version as the About window names it, the operating system as the runtime
describes it, and which plugins loaded and which sound backend opened. Nothing
else — no patch, no file name, no folder. The plugin folder in particular is
left out, since its path carries the person's account name.

This is the difference from ADR-0094 and why it does not breach it: a count
nobody asked for has to be anonymous, but a letter is written on purpose, and
what goes with it is read on the way out. That is also why the patch is not
attached, however useful it would be for a bug: it is the person's work, and
sending it is a separate thing to ask for.

**The button is the last thing on the status bar,** right of the module count.
The toolbar is what is done to the patch and to the program; a letter is
neither, and it is reached for once in a year. Its envelope is drawn rather
than typed, because Inter has no envelope — nor a gear nor a ringed i, which
the toolbar types anyway and which the platform's emoji font answers for.

## Consequences

- A letter reaches nobody until the site the copy points at is running and
  reachable. The shipped build points at `PresetSite.Local`, so today a letter
  goes nowhere; the same is already true of shared presets.
- There is no thread. A letter is read on the admin page and answered by mail,
  if there is an address, or not at all.
- Letters are not moderated before they are read, only rate-limited. The admin
  page is the only reader, so what arrives is the author's problem alone.
- Anything a person types could be anything, including something about
  themselves. The letter says what is added to it; it cannot say what not to
  write.
