# ADR-0135: A patch that names a plugin you do not have offers it

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0134](0134-a-plugin-declares-its-modules-and-is-refused-for-one-it-did-not.md),
whose declarations are what makes the lookup exact

## Context

A patch is refused whole where a module in it has no plugin
([0026](0026-modules-from-plugins-with-provenance-in-the-file.md)): opened with
holes it would compile to something other than what was saved, and the next save
would write the holes over the original. The refusal says what is missing, by the
provider's name and id, and stops there.

That was the whole of it, and it left the user somewhere they could not get out
of from inside Flyback. A patch shared by somebody else is the ordinary way to
meet a plugin you do not have — it arrives with the plugin's id in it and no way
to act on it. The plugins window has had the plugin listed all along; nothing
connected the two.

The obvious connection is a search: take the provider's name out of the refusal
and put it in the plugins window's box. It is also the wrong one. Words match
whatever reads alike, and a patch that will not open is the worst moment to put a
plugin in front of somebody that merely shares a word with the one they need —
the dialog behind the row says what a plugin reaches, and a plugin installed by
mistake is a plugin running inside Flyback with everything the user can do.

The exact key already exists on both sides. A plugin declares its modules by type
id ([0134](0134-a-plugin-declares-its-modules-and-is-refused-for-one-it-did-not.md)),
the site indexes them and answers `?module=`, and the patch holds the very ids it
cannot build.

## Decision

**The patch is matched by a module id, not by words.** For each provider a patch
is short of, Flyback asks the site for the plugin declaring one of the modules the
patch actually holds. An id names one plugin or none. A provider with no module of
its own left in the patch — a stale stamp — is not asked about at all, because
there is nothing exact to ask with.

**It offers, and installs nothing.** The answer opens the plugins window, where
what the patch needs has a section of its own above everything else — one search
box cannot ask for two plugins at once, since every word of it has to match, and
the search belongs to the user anyway. A plugin is installed the one way any is:
its row, and the dialog that says what it adds and reaches (ADR-0132). The site is
given three seconds; a site that is slow or down leaves the refusal as it was.

**The restart carries the patch back.** Installing means restarting, because
plugins are read once at startup, and coming back up without the patch that asked
for the plugin is the user doing the last step by hand. A file travels as the plain
argument a patch opened with Flyback already arrives as, so the launch that
receives it needs to know nothing about having been restarted. A preset from the
site has no file, so it travels as its id and is fetched again — the same code path
as opening it from the gallery, with the same name and the same absence of a folder
of its own. Only the window opened from the offer carries one, so installing
something unrelated reopens nothing.

**Only a deliberate open offers.** The four ways a patch is opened — a file, a
bundle, and both of those shared — ask. Restoring after a crash does not: it
happens at startup, before anybody asked for anything, and the work is kept for a
start that has the plugin again.

## Consequences

**A shared patch carries its own plugins in practice.** The one thing a patch
could not do for itself, it now does, without the file having to hold a URL or the
site having to be trusted for anything but a search and a fetch.

**A restart costs the preset site one request.** Reopening a shared preset asks the
site for it again rather than keeping the bytes somewhere across the restart, which
would need a file with no owner and a document whose next save went somewhere the
user never chose. Where the site has since dropped it, the window says so and opens
nothing.

**Declaring modules pays a second time.** [0134](0134-a-plugin-declares-its-modules-and-is-refused-for-one-it-did-not.md)
asked plugin authors to declare their modules so a listing could show them without
running code. The same declarations are now what makes a patch findable, so a
plugin that was published before declarations is offered for nothing.

**Nothing is offered for a plugin the site does not have**, including a private
one and one with no build for this system, which the site's own listing filters
out. The refusal stands by itself, as before.
