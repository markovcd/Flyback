# ADR-0062: MIDI input is polyphonic through indexed voices

**Status:** Accepted · 2026-08-30 · *user-directed* · replaces the monophonic
choice in [0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md)
and uses the declared field shape from
[0055](0055-a-plugins-extra-declares-its-editor.md)

## Context

[0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md) made a MIDI
input playable, but deliberately made it monophonic: one pitch, one gate and
last-note priority. That is sufficient for a single module, but it prevents a
patch from routing different simultaneous notes to different instruments.

The desired behavior is to place several MIDI In modules in a patch and give
each one an index. If modules are assigned indices 1 and 2, the first note
occupies voice 1, the second occupies voice 2, and releasing one note silences
only its voice. A later note reuses the first available voice.

The same behavior must apply to the computer keyboard. It is another MIDI
source, not a special monophonic exception, and its notes must be able to drive
multiple indexed MIDI modules in the same way as hardware input.

## Decision

**MIDI In carries a persisted `voice` index from 0 to 8.** Index 0 means
automatic assignment; explicit indices 1 through 8 reserve a particular voice.
The index is instance data in `MidiExtra`, alongside the selected device, and is
edited with the declarative numeric field mechanism.

**Live signal names include the source and voice index.** Voice 1 retains the
original `source/signal` spelling for compatibility; higher voices use
`source/index/signal`. This lets existing patches continue to read their first
voice while allowing multiple modules to read distinct voices.

**The hub owns 8 voice slots per source.** Each note-on is assigned to the
lowest free slot, note-off releases the slot holding that note, and an
all-notes-off event clears every slot for that source. The computer keyboard
and hardware devices use the same allocation path.

**Repeated note-ons for a held note are ignored.** Keyboard auto-repeat and
duplicate device messages do not create another strike or require another
note-off.

**Allocation is limited to indices used by the running patch.** If all configured
voices are occupied, the next note reuses voice 1 rather than being assigned to
an unobserved higher index. This makes a patch with two MIDI modules behave as a
two-voice instrument even though the runtime supports eight slots.

**Automatic modules receive stable per-module live keys.** The compiler includes
the node identity in an automatic voice key, and the hub maps automatic modules
to the remaining available voices in patch order. This keeps two automatic
modules independent while allowing explicit indices to reserve voices.

**Each MIDI module reads only its selected indexed voice.** The module's pitch,
gate, velocity and trigger signals are loaded from that voice; no subgraph
cloning or special polyphonic module is added to the compiler.

## Consequences

Multiple MIDI In modules can receive simultaneous notes without changing the
patch graph or adding a new module type. Their indices are explicit and saved
with the patch, so changing module order does not change which voice a module
reads.

Voice capacity is deliberately bounded at 8. Notes beyond the configured slots
reuse the first configured slot, preserving a bounded allocation and avoiding
silent notes routed to modules the patch does not contain.

The computer keyboard's old last-note-priority behavior is no longer the
contract. Existing patches that use only voice 1 retain their original live
signal names and behavior for single-note use, while patches can opt into
polyphony by adding modules with higher indices.

## Amendment, 2026-09-23: a voice can belong to one channel

A drum machine puts each of its tracks on a MIDI channel of its own, and the
voices above merge every channel of an instrument into one set, so a kick on
channel 1 and a hi-hat on channel 2 fought over the same eight voices and no
module could tell them apart. A MIDI In now has a `channel` field, 0 for the
whole instrument as before or 1 to 16 for one channel.

A channel is named as an instrument of its own — `midi:syntakt@3` — rather than
as a segment of the key, so the hub's voice sets, the automatic assignment and
the indexed keys all go on working unchanged over the longer name. A note on a
channel plays two voice sets, the instrument's own and the channel's, since a
module listening to the box and one listening to its channel 3 are both meant to
hear it. The computer's keys have no channels, so a channel asked of the keyboard
is reported and the keys are heard as before.

## Amendment, 2026-09-24: a busy voice keeps what it held

A note arriving with every configured voice busy still goes to voice 1, but no
longer silences it first. It sounds over what voice 1 held, and when it is let
go the voice falls back to the newest of those still down. That makes a patch
with one MIDI In play legato, the way a mono synth does, and gives a note back
its voice when the note that took it ends. A note let go while buried under
another is taken out wherever it is held, so it never comes back.
