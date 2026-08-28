# ADR-0060: A bundle is a patch and what it names

**Status:** Accepted · 2026-08-28 · *user-directed* · answers
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) and
[0059](0059-a-picture-comes-in-as-a-texture.md)

## Context

Two records gave up the same thing for the same reason.
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) made a patch
name its sounds rather than carry them, because the file is JSON held two hundred
deep in an undo stack and a megabyte of PCM in it would be a megabyte per undo
step; [0059](0059-a-picture-comes-in-as-a-texture.md) did the same for pictures,
where the numbers are larger. Both recorded the cost in the same words: a `.fbk`
is no longer everything it needs.

What that means in practice is that a patch sent to somebody arrives as a
document full of paths that mean nothing on their machine, and a patch put in a
repository is only half of what was made. Neither record was wrong. What was
missing is the other file.

## Decision

**A bundle is a second file rather than a change to the first.** `.fbk` is the
document, unchanged and still text; `.fbkp` is that document with everything it
names travelling beside it. Nothing about the undo stack, the file format or the
compiler moves — the reasons 0052 and 0059 gave are still true of the thing they
were about.

**It is an ordinary zip.** The format is in the framework, so this adds no
dependency ([0019](0019-no-third-party-dependencies-in-the-engine.md)); every
operating system already opens one, so a bundle a future build cannot read is
still a folder somebody can get their work out of; and the archive's own
directory is the manifest, so there is nothing to keep in step and no second
description of the contents to fall out of date.

**The patch inside names the copies beside it.** `patch.fbk` at the root,
everything else under `files/`, and every path in the patch rewritten to name its
copy. That is the whole trick: a relative path is already measured from wherever
the patch is, so **an unpacked bundle is a working patch with nothing else
arranged** — unpacked by this program or by double-clicking it in a file manager.
There is no `unpack` command for that reason.

**What goes in is asked of the modules.** `NodeExtra.Files` says what an instance
names and `NodeExtra.Rebase` points it somewhere else, so the packer mentions
neither WAV nor PNG and a third kind of file is carried without it being touched.
It is the same seam [0055](0055-a-plugins-extra-declares-its-editor.md) opened
for editors, used for a second purpose.

**A bundle is a document, not a copy of one.** Saving one marks the patch saved,
takes the name in the title bar, and is what the question about unsaved changes
accepts as an answer. The two kinds of file differ in what is in them and in
nothing else — which is the point of having a second one at all, since a format
you can only export to is a format nobody works in.

**Nothing is ever unpacked to open one.** Not into a folder beside the archive
and not into a temporary directory: the entries are held as they came, the folder
libraries stay behind them, and a module pointed at a file on this machine a
moment later means the file on this machine. Saving writes those same bytes back,
so a photograph is not re-encoded on its way through a session — which would
quietly make a sixteen-bit file an eight-bit one and bake in the transparency
that was multiplied away.

**Holding them costs the undo history nothing, and that is the whole reason it is
allowed.** The history is snapshots of the patch, the patch is paths, and
payloads sit beside the document exactly as the two decoded caches already do.
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)'s argument
was that a payload must not be *in* the document; it is not. What this does cost
is one copy of the compressed bytes for as long as the bundle is open, which is
what buys saving it back unchanged.

**Saving a bundle as a loose patch scatters what it was carrying.** The paths in
it are relative, so writing the files beside the new `.fbk` is all it takes for
the saved document to work — the exact inverse of packing, and the one place
saving writes more than the file it was given. Only what the patch still names,
and nothing already there is overwritten.

**The command line reads one and writes nothing at all.** No document, no
folder, no temporary anything: a build server holding one file draws a patch
whose photographs it has never seen.

**Nothing in the engine opens a file.** What to pack is asked for by path and
answered by the caller; what comes out is handed back as bytes for the caller to
put somewhere. The same division `ISampleLibrary` makes, and for the same reason:
this is the document's business and not the disk's.

## Consequences

**The window holds two libraries and looks in both.** A bundle in front of the
folder it was opened from nowhere near — the archive answers for what it holds
and the disk for everything else, which is what lets a file be added to a bundle
without unpacking it first. The one thing a bundle has no answer for is a
relative path it does not hold, and there is nothing sensible for that to mean:
a bundle has no folder to measure one from.

**A file that has gone is reported and the bundle is written anyway.** The same
call `check` makes: the patch opens, compiles and draws without it — to silence
or to black — so a bundle of it does too. What is refused is the claim to be
self-contained, which `pack` says with exit code 1.

**Two files of one name both fit.** Inside a bundle there is one folder, and two
folders may each hold a `drums.wav`; the second becomes `drums (2).wav`. Numbered
rather than made unique by hashing the path, because a bundle is a zip somebody
may open in anything and a name they recognise is worth more than a name that
cannot collide.

**A patch inside a bundle is one the packer read and wrote again.** It goes
through `PatchIo` both ways, which is what makes the copy deep — and means a
bundle written by a build missing a plugin carries what that plugin's modules
said without carrying their files. The alternative was to refuse, which would
lose more.

**What is still true is that a patch is text.** A bundle is not a project format,
not a workspace and not a place to keep settings. It holds a patch and the files
that patch names, and every further thing somebody might want in one is a reason
to look at this record again rather than a hole in it.
