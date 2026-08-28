# ADR-0059: A picture comes in as a texture

**Status:** Accepted · 2026-08-28 · *user-directed* · answers
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md)

## Context

Everything this program draws, it works out. A patch is a function of `(x, y, t)`
and the catalogue is arithmetic: there was no way to put a photograph in one, and
no way to put back a frame it had itself exported. The ear had been able to bring
something in from outside since
[0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) gave it a
sample player; the eye had nothing.

That record also said what it would cost and left the door open in as many words:
*"A patch that plays a sample on screen gives up the shader. That is the price of
the two backends agreeing… It is also the one part of this worth revisiting: a
clip uploaded as a texture would work on both, since `SampleFeedback` already
proves a texture read lowers to GLSL — and then nothing would have to stand down
at all."*

## Decision

**One new opcode, `SamplePicture`, and it is a texture at both ends.** On the
processor it is a bilinear read out of a float array; on the shader it is
`texture()` against a sampler the frame binds. That is the whole difference from
`Table`, and it is the difference between the two: a clip is a buffer a shader has
nowhere to put, and a picture is the thing a shader is made to read. So a patch
showing a photograph keeps the GPU, and nothing stands down.

**The picture is placed at its own shape.** It fills the height, reaches its own
aspect either side of the middle, and is black beyond all four edges. The
consequence is the property worth having: a frame this program rendered, read back
in, is in exactly the place it came from — the round trip through
`flyback-cli render` is byte-identical, and a test says so. What a patch wants
instead is a Scale, a Translate or a Warp in front of it, which is the same
division of labour the Sample makes about time.

**Black outside, not the edge held and not tiled.** Holding the edge smears the
last row across everything beyond it and reads as a fault; tiling is something a
patch says with a Tile. Running off the edge is how a picture ends, exactly as
running off the end is how a clip does — and it is the same rule transparency
gets, since alpha is multiplied in as the file is read rather than carried. Three
numbers is what a color is here, and a fourth would have been a second opcode.

**PNG, and the decoder is ours.** [0019](0019-no-third-party-dependencies-in-the-engine.md)
says no imaging dependency, and the writer at the other end of this was already
hand-written for that reason — so the two are a pair, and *what this program can
read is what it can write*. The hard half was never ours: `DeflateStream` is in
the framework and is where `PngWriter` already gets its deflate, so the reader is
chunk-walking, un-filtering and unpacking. Every color type at 8 and 16 bits;
interlaced files and sub-byte depths are refused by name rather than read wrongly,
which is the same call `WavReader` makes about compressed payloads.

**Which program may open a file is settled by what the compiler hands the walk.**
The screen's gets a picture library and the speakers' gets nothing, so an Image on
the audio path lowers to black without a file being opened or a complaint being
made about one that has gone. The module needs no way to ask which sink it is
being lowered for — the answer is whether it was given anything, which is the
mechanism the Scope's buffer already used, upside down.

**The path is a fourth typed field on the node.**
[0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) said this one would
be easier to argue for than the third was, so it is worth saying it meets
[0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)'s test rather than merely
being convenient: which picture a patch shows is a decision about the piece and
not a signal in it, and no arrangement of sockets says it, because a socket
carries a number and a path is not one.

## Consequences

**The GPU has textures it did not have.** One per distinct picture, bound from
unit one upward because nought is the previous frame's, uploaded when the pictures
change rather than when the shader text does — a photograph swapped for another of
the same shape is the same program and a different texture, and a knob drag is a
different program and the same texture. The aspect is a uniform rather than a
constant folded into the text, so choosing a picture of a different shape
recompiles nothing.

**And this is the part the tests cannot reach.** [0035](0035-a-glsl-backend-for-the-video-path.md)
recorded that CI is headless and the GPU is checked by hand; that is still true,
and there is now more of it to check. What the tests do hold is the text — that
the shader declares a sampler per picture and reads it through a helper written to
agree with the processor at every edge — and the whole of the processor's side,
including a frame exported and read back landing pixel for pixel where it left.
The upload itself is eight calls of the same kind the feedback pair already makes.

**A patch is less self-contained than it was, again.** 0052 gave up
self-containment for audio and this gives up the rest of it: a patch is now a
document that may name two kinds of file, and one that has moved is reported by
name rather than passed over. The libraries are two classes rather than one with a
type parameter — what they share is eleven lines of caching and what they do not
is the reader, the fault and every sentence a person is shown.

**The assistant gained a tool and the panel gained a row.** A path is neither a
knob nor a wire, so `set_picture` is the only way a model can finish an Image it
placed — the same argument `set_sample` was written under. The two file rows in
the inspector are now one method with four arguments, which is the refactor the
second one was going to force either way.

**What is still not here is a moving picture.** A video is a container and a codec
each, and [0019](0019-no-third-party-dependencies-in-the-engine.md) has not
changed — a decoder for one is a different order of work from a decoder for PNG,
where the compression came out of the framework. What a patch can do instead is
what it could already do: name a still, and move the *coordinates* rather than the
picture.
