# ADR-0151: A built preset carries its files in its assembly

**Status:** Accepted · 2026-09-25 · *user-directed* · answers
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) and
[0060](0060-a-bundle-is-a-patch-and-what-it-names.md) for presets written in C#

## Context

A patch names its sound files and pictures rather than holding them (0052), and a
bundle is how one travels with them (0060). A preset written in C# had neither: it
has no folder to measure a relative path from, so a Sample in one could only name a
file that happened to be on the machine. The Clip preset opens with no file chosen
for that reason, and a piece that needed recordings, like Tranquility, had to leave
the box for the preset site as a file (0138). Mycelium needed a voice and is a
class.

## Decision

**A preset may carry files, and they are resources of the assembly that ships it.**
`PatchPreset.Files` hands back the files by the path the patch names them by, and
`PresetFiles.Embedded(assembly, folder)` reads every resource under one folder. The
patch is unchanged: it names `who-are-you.wav`, as a bundle's patch names its copy.

**An opened preset is a bundle of them.** Every place a preset is opened, the
gallery, the viewer, `flyback-cli print --preset` and the canvas, wraps them in the
same `BundleFiles` a bundle's contents are read through, so nothing downstream of
opening learns there is a third kind. On the canvas the document is a bundle, and a
save keeps the files with it.

**An init property, not a constructor parameter.** Adding a parameter to the record
would change the constructor every plugin already calls, and a plugin built against
the old one would fail to load.

**Read when opened, not when registered.** The gallery lists presets far more often
than it opens one, and the bytes are the opened patch's to keep.

## Consequences

- A plugin's assembly grows by what its presets carry. Mycelium's six lines are about
  a megabyte of 22 kHz mono.
- `ShippedPresetTests` holds every preset to carrying exactly the files it names, so
  a renamed clip or a dropped line cannot ship half a pair.
- The files are the plugin's, so they are licensed with it. Anything carried has to
  be free to ship; Mycelium's are LibriVox's public-domain reading.
