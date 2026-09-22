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
has to be treated as hostile until someone decides otherwise.

What a plugin calls itself (`PluginInfo`) and what it registers are only known once
its code has run, and running it to fill in the question of whether to run it
answers the question.

## Decision

**A `.fbkp` is a zip of builds and nothing else.** A folder per system, named as
runtime identifiers begin — `win`, `osx`, `linux` — and `any` for a build that runs
everywhere, used where the system has no folder of its own. A folder is a build if
exactly one plugin assembly sits at its top: one with a public, concrete type
implementing `IFlybackPlugin` as `Flyback.Plugins` defines it, not an interface of
that name from anywhere else. There is no manifest. A description written beside
the plugin is a second account of it that can disagree with the first.

**What the dialog shows is read from the plugin's metadata, and none of its code
runs.** Its name, version, author and description come from the assembly's own
attributes (`Product`, `InformationalVersion`, `Company`, `Description`), as its
project set them. What it adds comes from which `IPluginRegistry` methods its code
calls: modules, presets, a sound output, a MIDI input, an assistant, a secret store.
What it reaches comes from what any assembly in the build names: the network, files,
other programs, the registry, native code (a P/Invoke, or a binary with no metadata),
and code it loads while running (reflection, `Emit`, a load context). The dialog
says that native code and loaded code can reach more than they name, and that the
name and author are whatever the author wrote.

**The dialog installs nothing on its own.** It shows the above, the systems the
package has builds for and the package's SHA-256, under a warning that a plugin can
do anything the user can. Install is off, with the reason written under it, where
there is no build for this system or its plugin was compiled against a contract this
Flyback does not offer. Escape, the cross and Cancel install nothing.

**Only this system's build is installed**, into `plugins/<plugin assembly>`, with
`package.sha256` beside it. That marker is what says a package put the folder there.
A package never replaces a folder without one — the plugins Flyback ships, or any
copied in by hand — and never brings a second copy of an assembly already loaded
from another folder. The host loads every folder with a marker after every folder
without one, so a package's plugin that claims an id already taken is the one
ignored.

**It is refused whole, without a dialog, if any entry could land elsewhere.** A
name that is rooted, climbs with `..`, holds a backslash or a colon (a drive, or an
NTFS stream), ends in a dot or a space, or is a Windows device name. Two names that
differ only in case. More than 4096 entries, 128 MB packed or 512 MB unpacked,
counted as the bytes come out rather than as the zip says. Every path is also
checked against the target after it is resolved.

**What the assembly says is cleaned before it is drawn.** Control characters and
the format characters that change how text around them is drawn — a right-to-left
override, a zero-width joiner — are dropped, and every field is cut to a length.
All of it is plain text.

**The package is read once, into memory.** What is installed is the bytes that
were shown, not whatever is at the path by the time Install is pressed.

**Installing unpacks into `plugins/.pending/<plugin assembly>`, and the next start
moves it into place** before anything is loaded. There is no reload, so a plugin
could not run sooner anyway; and a plugin being replaced is one this process has
loaded, which Windows will not let go of. A folder under `plugins/` whose name
starts with a dot is never scanned for plugins.

**The dialog offers to restart Flyback, ticked by default.** The window closes the
way any close does, asking about unsaved work, and a cancelled question or a
recording still running leaves it open, installed for the next start. The new
process is started with `--after <pid>` and waits for the old one to exit before it
looks at a plugin, since until then the one being replaced is still loaded.

**`flyback-cli pack-plugin` makes one**, and asks nothing the build already says. A
project is published with the SDK once for each runtime its `RuntimeIdentifiers`
names, or once portably where it names none. A folder the SDK built into needs no
SDK: `publish/` or the folder itself is the portable build, and each
`<rid>/publish/` or `<rid>/` is the build for that runtime's system. Two runtimes for one system
are refused, since a package holds one build for each. A build is held to the
project file the plugin guide asks for, as the build shows it: a plugin without the
`runtimeconfig.json` that `EnableDynamicLoading` writes was built without its own
dependencies, and a copy of `Flyback.Core` or `Flyback.Plugins` is a reference that
was copied or left unnamed. Either is refused, naming the property to set. A build
with no plugin in it says why: none of its assemblies references `Flyback.Plugins`,
or one does and has no class implementing `IFlybackPlugin`. Then it
reads the package back the way the editor will, writes nothing if the editor would
refuse it, and prints what the dialog will show.

**The viewer passes a `.fbkp` on to the editor**, so it reaches the editor whichever
program Settings → Files hands Flyback's files to.

## Consequences

Nothing is signed. A package's SHA-256 can be compared against what its author
publishes, and that is all. Signing would need somebody to trust a key, and there is
nobody to be that yet; a later decision could pin a key per plugin on first install
and refuse an update signed by another.

What the dialog says a plugin reaches is what its code names, not what it does.
Reflection and native code are named themselves, which is as far as reading can go.

The command line and the viewer move nothing into place: an install waits for the
editor's next start.

`plugins/` is inside the application, so installing needs write access there, and
on a Mac it changes the bundle. Where the folder cannot be written, Install is off
and says so.

A package's `win` build is used on every Windows machine, whatever its processor. A
plugin with native assets for one architecture still needs its own
`runtimes/<rid>` folders, as it would dropped in by hand.

There is no Gherkin scenario: the specs project reaches the engine, and this is the
editor and the command line.
