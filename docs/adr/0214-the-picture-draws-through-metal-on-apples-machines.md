# ADR-0214: The picture draws through Metal on Apple's machines

**Status:** Accepted · 2026-10-25 · *user-directed* · a second backend beside
[0035](0035-a-glsl-backend-for-the-video-path.md)'s GLSL, for the platforms
[0211](0211-the-editor-builds-for-ios-as-android-does.md) left on OpenGL ES

## Context

The iOS editor in the Simulator froze for minutes on Mycelium: the main thread sat in one
`glDrawArrays` while Apple's software GL allocated registers for its 63,000-character
shader. Apple's OpenGL and OpenGL ES, on a Mac and on an iPhone, are a deprecated layer
over Metal with no `KHR_parallel_shader_compile`, so 0035's background link never
happens there: the link and then the first draw stop the thread that draws the window.
On the same M2, Metal compiles and links that shader in half a second, and every preset
in about a second at most (Flythrough's 440,000 characters in 0.9 s), on any thread.

## Decision

**A Metal library from the same lines as the GLSL.** The helpers transcribing the
interpreter and the per-op lines are `ShaderText`'s, written once in GLSL's words.
`GlslEmitter` declares them as uniforms; `MetalEmitter` wraps them in a struct whose
members stand for those uniforms, with a prelude naming Metal's types after GLSL's and
supplying the few builtins Metal lacks. A fix to one backend is a fix to both, and
`MetalEmitterTests` fails on a preset whose lines differ.

**Fast math off.** The library is compiled with Metal's safe math mode: the helpers test
a NaN against itself and keep the clock in two floats, which fast math folds away.

**Stored as OpenGL stores it.** The patch's vertex function draws the quad upside down,
so the history and the planes hold the frame bottom row first. Feedback, planes and a
readback read the same rows on either backend, and the processor's picture is the
reference for both.

**Metal bound by hand.** `ObjC` reaches the Objective-C runtime through `objc_msgSend`
cast to each call's shape, and `MetalDevice` makes the dozen Metal calls the picture
needs. No binding package: the project is plain `net10.0`, the same assembly on every
platform, and the macOS and iOS workloads' bindings would split it.

**Headless first.** `MetalHeadlessRenderer` draws and reads frames back, and
`MetalRenderTests` runs every case of `GpuRenderTests` against the processor to the
same bounds. GitHub's macOS runners have Apple's paravirtual GPU, so the picture is
checked on a GPU on every push, where OpenGL there is off (0195's amendment).

**The preview reads its frames back.** `MetalPreviewSurface` draws on a thread of the
pool, as the processor's preview does, and shows the frame as a bitmap: a library compiles
in the frame that first draws it, and a slow one holds the last frame rather than the
window. Reading a frame back is a copy, small on unified memory, and a recording takes the
frame from there. A Metal layer composited by Avalonia would save the copy and is left
until the copy costs something measurable. `PreviewHost` builds this surface on
macOS and iOS, for the editor and the viewer; OpenGL stays everywhere else, and the
processor takes over where Metal will not open.

## Consequences

- A patch's shader is two texts from one source; a change to `ShaderText` is tested in
  both, and a declaration added to one emitter must be added to the other.
- The binding covers only what the picture calls. A new Metal call is a new signature
  in `MetalDevice`, checked by `MetalRenderTests` on a Mac.
- In the iPad Simulator, Mycelium's picture draws at 14 frames a second with the UI thread
  idle, where OpenGL ES held the UI thread for minutes.
