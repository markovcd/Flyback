# ADR-0140: An instrument is known by a profile file

**Status:** Accepted · 2026-09-23 · *user-directed* · builds on
[0086](0086-panel-knobs-are-read-as-live-values.md),
[0062](0062-indexed-polyphonic-midi-voices.md) and
[0139](0139-a-patch-keeps-to-an-instruments-clock.md)

## Context

Setting a patch up for a drum machine was the slow part of playing with one.
Every panel knob was learned by turning something on the box, one knob at a
time and again for every patch, and what came back was `CC74·3`, which says
nothing a week later. A MIDI In's channel was a number to remember, and the
Syntakt has thirteen of them. Nothing in Flyback knew what a Syntakt was, only
that a port had that word in its name.

The engine has no business knowing either. A patch stores a device id, a channel
and a controller number, and those are what the hub matches against the wire;
a name for any of them is a courtesy to the person, not a fact the program runs
on.

## Decision

**An instrument is a file.** `Instruments/syntakt.json` in `Flyback.Ui` says
what the Syntakt is: substrings its port is known by, that it conducts, its
tracks with their channels, and its knobs by page with the controller each
sends. The same shape describes any box, so a Digitakt or a Launchkey is a file
somebody writes rather than a feature somebody builds. A file of the same shape
in the user's `instruments` folder under Flyback's data folder is read too, and
replaces a shipped one of the same name, which is how a box set up on other
channels is described.

**The profile is read only where a name is shown or offered.** A MIDI In's
`channel` field is drawn as the instrument's tracks where the device is known,
and stored as the number it always was. A knob's menu offers "Bind to ›
Syntakt › Track 3 › Filter Frequency", which stores the same binding a learn
would. A binding is labeled by the profile wherever the panel shows one, and
the plain `CC74·3` where the device is not known. A fresh Clock In follows the
instrument that conducts, which the shell says by a flag on `MidiSource`, since
the engine's picker is built long before anything can read a file.

**The module list offers the instrument whole.** While a known instrument is
plugged in it is listed above the groups, and picking it adds a fragment built
from the profile for the port it is on: a Clock In where it conducts and a MIDI
In per track on its channel, named after the track, in columns of seven. Built
when picked rather than kept as a preset, because the one thing a preset could
not know is the device id the port has on this machine, and every module in the
fragment stores it. Loose rather than grouped: a group is drawn as one box with
every socket that crosses its edge, and a box with fifty sockets is not a thing
anybody wires from.

**A page belongs to a kind of track.** The Syntakt's FX track reuses controller
numbers its audio tracks give to other knobs, so a page names the kind of track
it is on and a track names its kind; a track with no kind has the pages with
none.

**A profile that will not read is skipped.** A file somebody is halfway through
writing is not a reason for the window not to open, and the settings' MIDI tab
says which instruments are known so the omission is visible.

## Consequences

A Syntakt patch is set up by picking the box from the module list, deleting
the tracks it will not use, and picking knobs from lists. Nothing about
it is stored differently, so a patch bound before this reads the same and a
patch made with it opens on a machine without the profile as numbers.

The profile matches by name, so a Syntakt whose channels were changed on the
box is described wrongly until its file is copied and edited. The settings tab
says where.

Learning a knob by turning it still works and is still the way to bind a
controller Flyback has no profile for.
