# A polyphonic wire

Written on 2026-10-02, on `main` at `2cd05423`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Kind:** Plan
- **Status:** Open, parked. Nothing is built; every point below is a design discussion, and the plugin side is unread.

## What is wanted

A wire that carries one signal per voice, so a patch plays chords through one chain instead of a copy of the chain per voice (today: several MIDI In modules at indices 1 to 8, [ADR-0062](../adr/0062-indexed-polyphonic-midi-voices.md), which declined cloning for MIDI voices only).

## Shape

- **Poly is a property of a wire, not a new `PortKind`.** The kinds stay `Scalar`, `Color` and `Any`. A wire is poly if its source output is, or if the module's output depends on a poly input; the compiler propagates it. The public API and the plugin contract do not move.
- **The compiler expands, the graph does not.** A lowering pass copies everything downstream of a poly source into N lanes, each with its own state slots. Mono modules never know. The IL, GLSL and interpreter backends see ordinary scalar programs, so ADR-0035's agreement tests still apply.
- **A merge module** sums the lanes back to one wire. Sinks (Output, the canvas) merge implicitly: sum for sound, add for the picture.
- **Lane count is a number on the source.** Two poly wires of different counts meeting run max(N) lanes; a missing lane reads as lane 0 or as zero (pick one in the ADR).
- **Sources:** MIDI In first, then a lane index (0 to N-1), then a fan-out module (a mono wire to N lanes with a per-lane offset), per-lane `Wander` seeded by the index, drum tracks.
- **Stereo stays two sockets.** `left` and `right` are identities, never summed; voices are interchangeable and are. Each side can be poly; a merge sums within a side. A stereo cable drawn as one thick line is presentation only. True multichannel would be a second axis, left out on purpose.
- **The picture is the same pass with a different merge.** N lanes of color per pixel: add (light mixing), max, or over (needs coverage, a bigger question). Per-pixel cost scales with N; what does not depend on a poly source stays shared. Voice 3's pitch and voice 3's ring should be the same lane.
- **The editor draws a poly wire differently** and shows its lane count. That is presentation, not type.

## Places a poly wire does not simply go

- **Shared-state modules:** delay lines, reverb, scope, recorder. Sum lanes first (one buffer) or clone per voice (N buffers); each module declares which.
- **Feedback loops:** a poly loop cloning its own state is fine; a loop crossing poly to mono is rejected or defined.
- **`Domain`, `Swept` and `PatchOnly` sockets** (flags on `PortSpec`): probably resolved once, not per lane. Unchecked how they compile.
- **Plugins:** a module is mono-in unless it declares itself lane-aware (a flag on its definition, the lane count known at compile time, an `IsPoly(socket)` query). Anything else gets a merge inserted in front, so a patch always compiles; the merge is visible in the patch.

## Open

- **Not read:** how a plugin module emits its ops. If its state lives in engine slots the compiler can clone it; if in its own fields, cloning needs a fresh instance per lane. Check before the ADR.
- Mono wire into a poly input, and poly into a mono input: broadcast and sum are the obvious pair; confirm.
- Fixed 8 lanes, or what the patch uses, as ADR-0062 does.
- Whether audio and picture share one voice index (the answer so far is yes).

## First step

Prove the pass with MIDI In, a lane index and a merge, on sound only. The fan-out module and the picture merge follow without redesign. Write the ADR first.
