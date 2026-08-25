# ADR-0052: A patch names its samples rather than carrying them

**Status:** Accepted · 2026-08-25 · *user-directed* · gives up the
self-containment [0020](0020-json-patch-files-keyed-by-string-type-ids.md)
established, and adds a third field to the instance data
[0051](0051-a-quantisers-scale-is-a-set-on-the-node.md) was wary of

## Context

Playing a recording is the one thing an instrument like this has no way to do,
and the reason is not the inner loop. `DelayState.Read` has always been an
interpolated read of a `float[]` at an arbitrary position — a playback head, in
everything but name — and delay lines already travel with a running program.
What was missing was a way to get audio into one of those buffers, and somewhere
for the audio to live.

The second is the whole decision. A patch is JSON, and
[`PatchHistory`](../../src/Flyback.Core/Graph/PatchHistory.cs) snapshots the
whole document on every edit, two hundred deep. Its own remarks justify that:
*"a patch is small enough that the trade is not close — the largest preset in the
box is twenty-six modules and a few kilobytes."* Base64 a five-second stereo clip
into the file and every knob turn re-serialises 1.3 MB, with two hundred of them
retained. Undo is what makes embedding expensive, not disk.

## Decision

**A patch stores a path.** `NodeInstance.Sample`, beside `Steps` and `Scale`. It
passes the test 0051 set for a third field of this kind — it is a decision about
the piece rather than a signal in it, and no arrangement of sockets expresses it,
a socket carrying a number and this not being one.

**A missing file is a compile error, named.** The compiler asks an
`ISampleLibrary` and turns a null into a complaint carrying the module's name and
the path, in the same list as every other thing it has to say about a patch. That
means it reaches the status bar, `flyback-cli check`'s exit code and the
assistant's issue list without any of them being taught about samples. A patch
with one still compiles, to silence where the recording would have been, so the
editor goes on drawing while the file is found again.

**The compiler does not read files.** Every edit recompiles (ADR-0021), so a
compiler that opened one would open it sixty times a second. `SampleLibrary`
caches by path — failures too, since a patch naming a file that is not there is
recompiled just as often as one naming a file that is.

**A relative path is measured from the patch.** So a patch and the sounds beside
it can be copied somewhere else together, which is the only thing that makes a
reference bearable. An absolute path is left alone.

**Position is a socket, not a transport.** `in` is how far into the clip to read,
in seconds, and it is a domain — so it is normalled to Time (ADR-0050) and a
player dropped on a canvas plays once, at the recorded speed, and stops. Rate,
direction, looping and scrubbing are all things you patch: a Saw times `length`
loops, a negative slope reverses, an envelope scrubs. The second output is the
clip's length, so a patch can do any of that without being told how long the file
is. This is the same argument 0048 made when it took the rate knob off Time.

**Audio only.** `OpCode.Table` reads silence where the program carries no clips,
and the compiler resolves them for the speakers' program alone. The interpreter
could perfectly well read a clip per pixel; the shader cannot without a texture
upload, and two backends disagreeing about what is on screen is worse than
neither drawing it (ADR-0035 allows them to differ in their last bits, not in
what they show). Delay and the ADSR are audio-only for a different reason and say
so the same way.

## Consequences

**A patch is no longer self-contained, and this is the cost.** Until now a `.fbk`
was everything: modules by id, plugins by name, and nothing else. One with a
Sample in it is a document plus a promise about the filesystem. There was no
version of this that avoided it — the alternative was breaking the undo model —
but it is a real loss and the reason the complaint is loud rather than quiet.

**A new opcode, which a plugin could not have added.** ADR-0041 found that a
plugin can hold state without one, because the cells it hands out are enough for
an integrator or a latch. A second of audio is not a cell, so this had to be an
engine change. That is the boundary 0041 drew, found from the other side.

**The instance data is now three fields.** 0051 warned that the next would be
easier to argue for, and it was. The test held: a path is a decision about the
piece, and it is not a number. It is worth saying that the test has now refused
nothing, so its usefulness is still unproven — the first thing it turns down will
be the evidence that it is a rule rather than a description.

**Mono, and WAV only.** The op is scalar like every signal here, so a stereo file
is summed on the way in. `WavReader` handles 8 to 32 bit PCM and 32 or 64 bit
float, walks chunks rather than assuming the audio starts at byte 44, and gives
back what was there when a file is truncated. Compressed payloads are refused by
name — they are a codec each, and ADR-0019 says no dependencies.

**The picture cannot show a sample.** Not even in a Probe, which compiles as a
video program. That is the price of the two backends agreeing, and it is the one
part of this worth revisiting: a clip uploaded as a texture would work on both,
since `SampleFeedback` already proves a texture read lowers to GLSL.
