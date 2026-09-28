# ADR-0157: flyback-cli render draws on the GPU

**Status:** Accepted · 2026-09-29 · *user-directed* · implemented in
`src/Flyback.Gpu/`, `RenderCommand.cs`, `MovieRenderer.cs` and the Dockerfile ·
amends [0035](0035-a-glsl-backend-for-the-video-path.md)'s export and its tests

## Context

`flyback-cli render` drew every frame on the processor. On a heavy patch the picture
was most of an export: Whole band at 1080p cost 42-45 ms a frame on every core,
while the preview draws the same picture on the GPU in a few.

The GPU renderer could not be reached from the CLI. `GpuFrameRenderer` called
OpenGL through Avalonia's `GlInterface`, and `flyback-cli` has no Avalonia so that
it runs on a machine with no display, no fonts and no X libraries. OpenGL itself
needs neither Avalonia nor a window: EGL makes a context with no surface on Linux,
and WGL makes one on a window nobody shows.

[0035](0035-a-glsl-backend-for-the-video-path.md) kept export on the processor
because the two backends differ in their last bits and an export should be the
reproducible one. It also left the GPU untested, since CI had no GPU and no screen.

## Decision

**The GPU renderer is a project of its own, with an OpenGL binding of its own.**
`Flyback.Gpu` holds `GpuFrameRenderer`, `GpuReadback` and `Gl`, a table of the
entry points the renderer calls, found by name through whichever context is
current. The preview hands it Avalonia's lookup; the CLI hands it a headless
context's. It references Core and Engine and no package.

**A headless context is EGL on Linux and WGL on Windows.** EGL tries Mesa's
surfaceless platform, then the first device EGL enumerates, then the default
display, and asks for desktop OpenGL 3.2 core before ES 3.0. WGL registers a class,
opens a 1×1 window it never shows, and asks for a 3.2 core profile. macOS has none.

**A render draws on the GPU where there is one, and on the processor where there
is not.** The shader is built before the first frame, so a patch the GPU refuses is
drawn on the processor from the start rather than halfway through, with one
warning line saying why. `--processor` asks for the processor, which is exact to the
bit; `--gpu` fails rather than fall back. `--interpreted` is a processor setting and
is refused beside `--gpu`. A render waits for each link instead of handing it to
the driver's threads, and reads each frame back as it is drawn.

**The gate draws on llvmpipe.** The Dockerfile's gate installs Mesa's EGL and
drivers, so the GPU tests run in CI through surfaceless EGL with no display and no
DRM device.

## Consequences

Whole band, 5 seconds at 1080p and 30 frames a second into an MP4, on an RTX 4070
SUPER: 7.1 s on the processor, 4.0 s on the GPU. The picture's share is now about
10 ms a frame including the encode. Of the rest, 1.9 s is the sound, which is what
a heavy export waits on now.

Every built-in preset's picture on the GPU is within 1 of 255 of the processor's at
the worst pixel, and within 2 for one; the tests hold it to 8 at worst and 0.5 on
average, which a picture drawn upside down, with red and blue swapped, or a frame
behind breaks. An export on the GPU is the same file twice on one machine, but not
from one driver to another, and not the processor's file. The processor's file is
one flag away.

The preview's GL calls go through `Gl` rather than Avalonia's binding, which is one
more thing to keep in step with the context's version: an entry point the renderer
needs and the context lacks is named in the failure rather than crashing.

The CLI loads `libEGL.so.1` or `opengl32.dll` only when a picture is rendered, so a
machine with neither still renders, on the processor.
