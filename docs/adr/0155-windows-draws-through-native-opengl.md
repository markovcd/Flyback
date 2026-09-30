# ADR-0155: Windows draws through native OpenGL

**Status:** Accepted · 2026-09-28 · *user-directed* · implemented in
`GraphicsDrivers.cs`, `OutputSettings.cs`, both programs' `Program.cs` and
`OutputSections.cs` · amends [0035](0035-a-glsl-backend-for-the-video-path.md)'s
Windows path

## Context

Avalonia draws on Windows through ANGLE by default: OpenGL ES translated to
Direct3D 11, every shader rewritten as HLSL and compiled by Microsoft's FXC. It is
the default because Direct3D drivers are dependable on every Windows machine and
OpenGL drivers are not.

FXC is slow on long shaders, and a patch's shader is long. Mycelium's is 63,000
characters. Measured on an RTX 4070 SUPER:

| | ANGLE | Native OpenGL (WGL) |
|---|---|---|
| Link, on the driver's threads | ~8 s | ~1 s |
| Window held when the program goes in | 7-8 s | ~0.3 s |

The second row is ANGLE compiling the pixel shader again inside the first draw.
Its link builds one output layout, for the shader's first output; a patch with a
loop draws to planes as well ([0074](0074-a-cell-is-a-plane-on-the-video-path.md)),
the draw asks for a layout the link never built, and FXC runs on whichever thread
draws. On Windows that is the UI thread, so every edit to such a patch froze the
whole window for as long again as the link took, once the link was done.

## Decision

**Windows draws through the graphics card's own OpenGL, and through ANGLE where
that will not start.** Avalonia tries the modes it is given in order, so the
fallback costs nothing to write: `Wgl`, then `AngleEgl`, then software.

**The driver is a setting, read once as the program starts.** Settings → Graphics →
Driver, Windows only, OpenGL or Direct3D, kept in `output.json` beside the rest of
the machine's settings. Direct3D is there for a machine whose OpenGL misbehaves
rather than refuses, which the fallback cannot see. Avalonia is handed the driver
before it opens a window and cannot change it afterwards, so the choice applies
from the next start. The editor and the viewer read the same file and the same
setting.

## Consequences

A large patch's picture is built in about a second on Windows, and an edit to a
patch with a loop no longer holds the window. The desktop dialect (GLSL 1.50) is
what Windows now runs, as Linux and macOS already did, so the three platforms run
one dialect's shaders where they had two.

Every Windows install moves to a path Avalonia does not default to. An Intel or
AMD OpenGL driver that starts but draws wrongly is not caught by the fallback, and
the setting is the way out; that it exists is said in its tooltip.

Nothing on screen says which driver a run ended up on.

## Amendment, 2026-09-30: one Renderer box, and the driver named on screen

Settings → Graphics has one Renderer box where it had Render (GPU or CPU) and
Driver: OpenGL, Direct3D on Windows, then CPU. `output.json` keeps `gpu` and
`driver` as they were; the CPU row leaves `driver` alone, because the window is
drawn through it whatever draws the picture. The box stays enabled after the
graphics card fails, since the other driver at the next start is the way out, and
saving a new driver says that it takes over from the next start.

The status bar, the stats line and the web editor's `status()` name what drew the
last frame, read from the context rather than the setting: `GL_RENDERER` naming
ANGLE is Direct3D (or Vulkan or Metal), any other context is OpenGL or OpenGL ES,
a page is WebGL. A Direct3D fallback on a machine set to OpenGL is visible now.
