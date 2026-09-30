# OpenGL failing after start drops to the processor, not Direct3D

Found on 2026-09-30, on `main` at `a9aee9eb`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Severity:** Low
- **Status:** Open

## What is wrong

The desktop's drivers are meant to fall back OpenGL, then Direct3D, then the processor.
That holds only at startup: `GraphicsDrivers.Modes` hands Avalonia
`[Wgl, AngleEgl, Software]`, so a machine where OpenGL will not open gets Direct3D.
Once OpenGL has opened, a failure of the GPU preview goes straight to the processor for
the rest of the session.

## Evidence

Read in the code, not reproduced on a failing machine:

- `GpuPreviewSurface.Fail` fires for an OpenGL older than the shader backend needs
  (`GpuFrameRenderer.CanRun`), `GpuFrameRenderer.Initialise` refusing, the context lost
  more than `TolerableContextLosses` times, and a render error.
- `PreviewHost.OnGpuFailed` withdraws the GPU for the session and activates a
  `PreviewSurface`, the processor.
- Avalonia picks its rendering mode once per process, so Direct3D (ANGLE) cannot take
  over without a restart.

## Impact

A machine whose OpenGL opens but misbehaves draws on the processor, which is slow on a
heavy patch, where Direct3D would have drawn on the GPU.

## Suggested fix

- On a failure while the driver is OpenGL on Windows, save `GraphicsDriver.Direct3D` in
  the output settings for the next start, say so on the status bar, and offer a
  restart. The processor draws until then.
- Leave a shader that will not build for one patch (`SetPatch`'s compile error) out of
  it: that is the patch, not the driver, and ANGLE compiles the same GLSL ES.
- Do not switch when the user chose OpenGL in Settings after Direct3D had been saved
  this way, or the two would flip at every start; one automatic switch, remembered.
- A Gherkin scenario with a stand-in surface that fails under OpenGL, checking the saved
  driver and what is said.
