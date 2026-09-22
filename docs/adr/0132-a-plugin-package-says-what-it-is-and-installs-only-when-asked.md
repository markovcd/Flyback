# ADR-0132: A plugin package says what it is, and installs only when asked

**Status:** Accepted · 2026-09-22 · *user-directed* · builds on
[0025](0025-platform-io-behind-loadable-plugins.md) for the folder a plugin is
loaded from, [0028](0028-publish-one-platform-at-a-time.md) for what a platform
is called, and [0102](0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md)
for the contract a plugin is checked against

## Context

Installing somebody else's plugin meant finding `plugins/` beside the executable
(inside the bundle, on a Mac), making a folder, and copying the right build's files
into it. Nothing said which build was the right one, and nothing said what the
plugin was before it ran.

A file that installs a plugin on a double-click is a way to put code on somebody's
machine that looks like opening a document. A plugin runs in-process with full
trust ([0025](0025-platform-io-behind-loadable-plugins.md)): whatever it is handed,
it can also read every file the user can and reach any address. So the package
has to be treated as hostile until someone decides otherwise, and everything it
says about itself as a claim.

## Decision

**A `.fbkp` is a zip.** At its top is `plugin.json` — `id`, `name`, `version`,
and optionally `author`, `description` and `website` — and beside it a folder per
system, named as runtime identifiers begin: `win`, `osx`, `linux`, and `any` for a
build that runs everywhere. A folder counts only if it has a plugin assembly at its
top, picked the way `PluginHost` picks one.

**Opening one shows it, and installs nothing.** The editor puts up a dialog with
what the manifest says, the systems it was built for, what it would add and the
package's SHA-256, under a warning that a plugin can do anything the user can and
that nobody has checked what it says. Install is off, with the reason written
under it, when the package has no build for this system, or when its build was
compiled against a contract this Flyback does not offer — read from the assembly's
metadata, never loaded. Escape, the cross and Cancel all install nothing.

**Only this system's build is installed**, into `plugins/<id>`, with the manifest
beside it. `any` is used where the system has no folder of its own.

**It is refused whole, without a dialog, if any entry could land elsewhere.** A
name that is rooted, climbs with `..`, holds a backslash or a colon (a drive, or an
NTFS stream), ends in a dot or a space, or is a Windows device name. Two names that
differ only in case. More than 4096 entries, 128 MB packed or 512 MB unpacked,
counted as the bytes come out rather than as the zip says. Every path is also
checked against the target after it is resolved, so the list above is not the only
thing standing between a package and the disk.

**The id is the folder's name**, so it is ASCII letters, digits, dots, dashes and
underscores, starting and ending with a letter or digit. A package never replaces
a folder it did not install — the manifest beside the plugin is how that is known —
so the plugins Flyback ships and any copied in by hand cannot be taken over. It
may not take the id of a plugin loaded from another folder.

**What the manifest says is cleaned before it is drawn.** Control characters and
the format characters that change how text around them is drawn — a right-to-left
override, a zero-width joiner — are dropped, and every field is cut to a length.
All of it is plain text; nothing in it is a link. A website is kept only as http or
https with no user name before the host, and the host is shown in ASCII, so a
lookalike letter shows as `xn--`.

**The package is read once, into memory.** What is installed is the bytes that
were shown, not whatever is at the path by the time Install is pressed.

**Installing unpacks into `plugins/.pending/<id>`, and the next start moves it into
place** before anything is loaded. There is no reload, so a plugin could not run
sooner anyway; and a plugin being replaced is one this process has loaded, which
Windows will not let go of. A folder under `plugins/` whose name starts with a dot
is never scanned for plugins.

**The viewer passes a `.fbkp` on to the editor**, so it reaches the editor whichever
program Settings → Files hands Flyback's files to.

## Consequences

Nothing is signed. A package's SHA-256 can be compared against what its author
publishes, and that is all. Signing would need somebody to trust a key, and there is
nobody to be that yet; a later decision could pin a key per id on first install and
refuse an update signed by another.

The command line and the viewer move nothing into place: an install waits for the
editor's next start.

`plugins/` is inside the application, so installing needs write access there, and
on a Mac it changes the bundle. Where the folder cannot be written, Install is off
and says so.

A package's `win` build is used on every Windows machine, whatever its processor. A
plugin with native assets for one architecture still needs its own
`runtimes/<rid>` folders, as it would dropped in by hand.

There is no Gherkin scenario: the specs project reaches the engine, and this is the
editor.
