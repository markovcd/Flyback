# ADR-0056: A patch can be played, and what plays it is one opcode

**Status:** Accepted · 2026-08-26 · *user-directed* · uses the third field shape
[0055](0055-a-plugins-extra-declares-its-editor.md) said to wait for, and adds the
first op since [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md)
argued none was needed

## Context

Everything a patch could read came from inside it. A program is a pure function of
x, y and t ([0005](0005-compile-to-a-flat-register-machine.md)), plus what it
remembered from the evaluation before
([0027](0027-delay-lines-give-the-audio-path-a-memory.md)), plus a clip settled
before it ran ([0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)).
A sequencer's tune is eight knobs; a Sample's file is a path. Nothing in the
catalogue could be told anything while it ran.

So the instrument could not be played. It could be programmed — a Note Sequencer
round a clock, an envelope off its gate — and every patch that made a melody made
the same melody every time. Wanting to hold a key down and hear it is not an
exotic request of a synthesiser, and there was no shape in the engine that
answered it even slightly.

Three things had to be decided, and only the first is obvious.

**Where a live value enters the program.** Two existing mechanisms nearly fit and
neither does. `OpCode.Table` reads something from outside the register file, and a
Scope already has something outside refilling one every frame
([0053](0053-a-scope-records-what-the-speakers-played.md)) — but a table read is
interpolated and positional, so five signals would have to be smuggled into one
buffer at positions chosen not to blend, and the GLSL backend lowers `Table` to
`0.0` and always will. `UnitRead` is a cell the program writes and reads, and
nothing outside can reach it.

**Whether the picture is played too.** The cheap answer is to ride on the audio
path's per-run state, which the video path passes as null — a MIDI module would
then be silent to the eye exactly as a Delay is a wire to it. The cheap answer is
also wrong for this instrument. Flyback is a video synth whose sound and picture
are two sinks over one patch ([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md)),
and "make the visuals react to what I play" is the first thing anybody will try.

**How many notes at once.** A polyphonic module is not a bigger version of a
monophonic one. It is four sets of outputs and a patch built four times over by
hand, because nothing in the engine clones a subgraph.

## Decision

**One opcode: `LoadLive`, and a block of floats passed to `Evaluate` beside the
delay lines.** It is the fourth kind of thing a program can read and the first
that can change under it while it runs. `K` is a position in
`CompiledPatch.LiveInputs`, which holds *names* — `keyboard/pitch` — rather than
numbers, because the two ends of this never meet: a module asks for a signal while
it is being compiled, and something in the shell fills it in when a hand moves.
An index would have to be agreed by counting, twice, in places that do not know
about each other.

Adding an op is exactly what 0041 argued was usually unnecessary, and the argument
still holds — that record was about *state*, and a cell answers for state. This is
not state. Nothing in the program produced it and nothing in the program can, so
there was no existing op to reach for.

**Both sinks, on both backends.** The interpreter takes a `LiveValues?` and the
shader takes a `uLive[]` uniform uploaded per frame — beside `uK[]`, which is the
same idea for a value that changes when the patch is edited rather than while it
is being watched. One frame is drawn with one reading of the keys, and one audio
buffer is filled with one reading, so nothing is ever half of two moments. What
is *not* played is an offline render: an export has nobody at the keys, and every
live input there reads nought.

**Monophonic, last-note priority.** A new key takes the voice; letting it go hands
the voice back to whatever is still held. That is what a rack with one pitch and
one gate has always done, and polyphony is deliberately left as a different module
rather than a switch on this one.

**A pulse is made inside the program, not handed to it.** The shell cannot send an
edge: "high for one evaluation" means 5 µs to the ear and a sixtieth of a second
to the eye, off the same block, and whoever is filling it in knows about neither.
So what arrives is a *count* of notes struck, which only ever goes up, and the
module differences it against a cell — each path finding its own edge at its own
rate. The count lives in a clock cell rather than a signal one
(`OpCode.ClockWrite`): a signal cell is clamped to the rails, the sixteenth note
of a session would pin it, and the trigger would stick high for the rest of the
day.

**The gate closes for the evaluation a new note lands on.** That one-evaluation
gap is what makes an ADSR articulate the second note of a legato run instead of
sliding through it, and it costs nothing but the multiply that makes it.

**Which instrument is a `Choice`, which is 0055's third field shape.** That record
said the vocabulary would grow when a module was actually blocked rather than when
one was imagined, and named "a choice" as the first thing to expect. This is that
module. The list is computed on every read rather than held, because what is
plugged in changes while the panel is open — the one thing `Number` and `Toggle`
never had to do. A stored id that is not in the list is *kept and reported*, never
quietly replaced: a patch written with a keyboard on it still means that keyboard
on a machine without one, which is `SampleExtra`'s bargain with a missing file.

**The instruments come from the shell, through a static.** `MidiSources.Install`
is asked afresh every call, unlike `NodeCatalog.Install`, which is frozen. It is a
static for a hard reason rather than a convenient one: what needs the list is a
`NodeExtra` hanging off a `NodeDef` built in a static constructor, long before
there is a window or a plugin, and there is nowhere to hand it in.

**One backend ships, and it is the one that needs no driver.** The computer's own
keyboard, read as two octaves of a piano in the tracker layout, mapped by physical
key so it stays a piano on a keyboard whose letters are somewhere else. Hardware
is `IMidiSource`-shaped work behind [0025](0025-platform-io-behind-loadable-plugins.md)
and [0028](0028-publish-one-platform-at-a-time.md) that has not been done; the
picker is built for it and lists what there is, which today is one thing.

**Amended: hardware arrives, and the shape above was right.** `IMidiInput` and
`IMidiPort` split the way `IAudioOutput` and `IAudioDevice` do — what might exist
against what is open — and `Flyback.Plugins.WinMidi` is the first backend behind
them, `Platform="win"`, over the multimedia library Windows already has. CoreMIDI
and the ALSA sequencer are the other two thirds; the sequencer is written now, in
the amendment below, and CoreMIDI is not.

Nothing above the hub changed to accept it, which is the part worth recording:
the picker already listed whatever `MidiSources.All` said, the patch format
already stored a string, and the module already compiled to whatever that string
named. What was added below the line is a second kind of instrument and the
question of when to open one.

**A device is held only while a program is reading it**, which is the same rule
the computer's keys already followed and now matters more. A MIDI In on the
canvas wired to nothing has been eliminated from both programs, so it opens
nothing — the keyboard stays available to whatever else is using it, and a patch
becomes an instrument the moment it is wired to one rather than the moment it is
drawn. Recompiling reconciles the two: devices a program reads are opened,
devices it no longer reads are handed back.

**Losing the focus stops meaning "let everything go".** It never should have
meant that for hardware: a MIDI keyboard goes on playing while another program is
in front, and the window has no business cutting a held chord off because
somebody alt-tabbed. It still means it for the computer's keys, which is the case
it was written for — a key released over another program is a key this never
hears about.

**The note now arrives on a thread nobody here owns.** A driver calls in on its
own, so the hub is guarded by a lock the playing thread never takes —
`LiveValues` is single floats and was built for exactly that. The rule that keeps
it from deadlocking is written where it can be seen: a device is never opened or
closed with that lock held, because closing one waits for the driver's thread and
the driver's thread may be waiting for the lock.

**Amended: Linux, through the sequencer rather than the raw device.**
`Flyback.Plugins.AlsaMidi`, `Platform="linux"`, beside the sound plugin
[0029](0029-linux-sound-through-alsa.md) put there and for the same reason —
ALSA is what is underneath whatever else the machine is running.

There are two ALSA MIDI interfaces and the choice between them is not close.
`snd_rawmidi` is bytes off a piece of hardware, which is fewer entry points and
is only the hardware: a keyboard plugged into the machine, and nothing else.
The sequencer is the kernel's routing table, and a keyboard, another program's
output, a virtual port and PipeWire's MIDI bridge all appear in it alike. The
deciding fact is the same one 0029 turned on: choosing the layer underneath
means being routed by whatever is above it rather than requiring that thing to
be absent. What it costs is a longer binding — the sequencer has no "how many
devices" call, so what is plugged in is found by walking every client and every
port — and a decoded event instead of three bytes, which `snd_midi_event_decode`
turns back into the three bytes `MidiMessages.Of` already reads. That call is
also what keeps `snd_seq_event_t` from being described in C# at all: it arrives
as a pointer and leaves as bytes, and nothing here knows where its unions fell.

**The reader polls, at a millisecond, rather than blocking.** This is the third
backend and the second that has to own a thread, and it is the only one where
the obvious loop is wrong. A blocking `snd_seq_event_input` cannot be closed: a
device is handed back whenever the patch is rewired, and a thread parked in a
read on the handle about to be closed is a crash, while a thread parked waiting
for a note that may not come for an hour never learns it was asked to stop —
0029's writer checks its flag every period, and this one would check it never.
So the sequencer is opened non-blocking and the reader wakes every millisecond,
which makes closing a port the matter of setting a flag it is on every other
backend. A millisecond is a fortieth of the block the sound path already asks
for; it is not where the latency of this program is.

**Listing what is plugged in opens a sequencer client, and that is still not
opening a device.** 0025's rule is that enumerating must not touch hardware, and
this does not: a client is a row in the routing table, it takes no card, blocks
nobody, and is closed again before the list is returned. Subscribing is the call
that would claim a keyboard, and only `Open` makes it.

**A port is shown as the device's name, joined to the client's only when it does
not already contain it.** The kernel usually names a card's ports after the card
— "Launchkey Mini MK3" holding "Launchkey Mini MK3 MIDI 1" — and printing both
unconditionally would stutter on every hardware device to spell one port called
plain "MIDI 1". What a patch stores is the id `MidiPorts.Named` makes of that
display name, exactly as on Windows, so the two backends agree about what an
instrument is called without either knowing about the other.

## Consequences

**A patch is a thing you can perform, and the picture performs with it.** That is
new, and it is the whole point. It also means the preview now has a reason to
redraw that has no time behind it — a key going down while the clock is stopped —
so `IPreviewSurface` gained a way to be told so.

**A program that is being played is not reproducible, and says so.** Every other
input to a render is in the file. This one is a person, and a movie of a patch
holding a MIDI In draws it with nothing held. Saying that in the module's own
description is the whole of the mitigation, and it is the honest place for it: the
alternative would be recording a performance into the patch, which is a different
feature and a much larger one.

**The computer's keyboard is an instrument only while something is listening.**
Whether the letters play notes or edit the patch is answered off the *compiled
programs* rather than off the canvas, so a MIDI In wired to nothing takes no
keystrokes — dead-code elimination ([0022](0022-audio-and-video-are-two-sinks-over-one-patch.md))
has already worked out whether anything reads it, and asking the patch would be a
second, worse answer. Text boxes are excluded by hand, because a text box does not
mark an ordinary key press handled and naming a patch would otherwise play a tune.

**A note is a bare keystroke, and every gesture on a letter now carries `Ctrl`.**
Undo, redo, lay out and the whole clipboard land on letters this layout plays, so
a modifier is what tells a command from a note — and framing moved off `F` onto
`Ctrl+F` to leave the rule without an exception. `F` was not in the shipped
layout and so was not yet a conflict; a gesture that held only until somebody
added a key to the layout is not one worth keeping. `Shift` is deliberately not a
command modifier, since no gesture here is Shift and a letter.

**The window's dropdowns stopped answering the keyboard.** A ComboBox reads a
keystroke two ways — an arrow moves the selection, a letter jumps to an entry —
and both commit rather than merely highlighting. That is ordinary behaviour for a
list of harmless options and wrong for every list here: the patch list *replaces
the document*, so an arrow held down at it discards the patch once per step, and
the size list is spelled in digits, which this layout plays as surely as the
letters. A `Picker` ignores both rather than swallowing them, so what it declines
to read goes on to the window and still plays. It is the last place in the shell
where a bare keystroke did something, which is why it is here rather than
recorded as an unrelated fix.

**Releasing a key is guarded by nothing at all.** Not the modifier check, not the
text-box check, not whether anything is still listening. A key going down can
start something and has to be sure it was meant; a key coming up can only ever
stop one, and releasing a note that was never played does nothing. Every guard in
front of a release is a way for one to be missed, and a missed release is a note
that sounds until the window loses the focus.

**Two modules on one instrument share one register**, the way two on one clip
share a table. That is not only a saving: two MIDI Ins reading one keyboard have
to agree about what it is doing, and sharing the register is how they cannot fail
to.

**The block is per-program and swapped with it**, exactly as the delay lines are —
a callback still rendering the previous program must be reading that program's
block. So a recompile makes a new one, and whatever is held has to be written into
it at once, or every knob turned while playing would cut the note off.

**"Nobody is playing" is nought, and it is nought in both places.** A program with
no block reads every live input as nought, and an idle voice in the shell rests
there too. Resting the voice at middle C was tried first and is the more
comfortable number; it also makes the picture on screen differ from the picture
the same patch exports, which is two answers to one question. Nothing is
protected by the friendlier number anyway — a patch reading the pitch without a
gate is silent, and the first key pressed moves the pitch without a click,
because an oscillator carries its phase across a change of frequency
([0030](0030-oscillators-accumulate-their-phase.md)). What it costs is that a
patch drawing something from the pitch alone rests at note nought, which is not a
note anybody strikes: the **Played** preset holds it into the range it draws
rather than leaving it there.

**`ExtraField` is a three-word vocabulary now, and the pressure to add a fourth is
real.** A path and a list of records are still imagined rather than blocked, and
the rule 0055 set stands: the next one waits for a module that cannot be written
without it.
