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

**MIDI In carries a persisted, 1-based `voice` index from 1 to 8.** The index
is instance data in `MidiExtra`, alongside the selected device, and is edited
with the declarative numeric field mechanism. A fresh module remains voice 1.

**Live signal names include the source and voice index.** Voice 1 retains the
original `source/signal` spelling for compatibility; higher voices use
`source/index/signal`. This lets existing patches continue to read their first
voice while allowing multiple modules to read distinct voices.

**The hub owns 8 voice slots per source.** Each note-on is assigned to the
lowest free slot, note-off releases the slot holding that note, and an
all-notes-off event clears every slot for that source. The computer keyboard
and hardware devices use the same allocation path.

**Each MIDI module reads only its selected indexed voice.** The module's pitch,
gate, velocity and trigger signals are loaded from that voice; no subgraph
cloning or special polyphonic module is added to the compiler.

## Consequences

Multiple MIDI In modules can receive simultaneous notes without changing the
patch graph or adding a new module type. Their indices are explicit and saved
with the patch, so changing module order does not change which voice a module
reads.

Voice capacity is deliberately bounded at 8. Notes beyond the available slots
reuse the first slot, preserving a bounded allocation and avoiding unbounded
state when a device sends unmatched note-ons.

The computer keyboard's old last-note-priority behavior is no longer the
contract. Existing patches that use only voice 1 retain their original live
signal names and behavior for single-note use, while patches can opt into
polyphony by adding modules with higher indices.
