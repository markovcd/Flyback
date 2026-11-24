# VJ output: the picture into Resolume and other VJ software

Written on 2026-11-23, against `main` at `7f48fe5`. It is on TODO.md; take it off there, and delete this file, in the commit that lands it. Nothing here has been built or measured; the effort figures are estimates, and the licenses should be re-read before anything ships.

- **Kind:** Plan
- **Status:** In progress: the picture output with pixels and time, `flyback-cli send`, the editor's Send tab and the NDI plugin's picture have landed (ADR-0240), untried against a real NDI runtime; NDI's sound backend, the texture, Syphon and Spout are left

## What is wanted

A DJ/VJ uses Flyback as a visualizer for music played elsewhere (an audio-reactive patch hearing the room through Line In). Today the picture reaches Resolume only by capturing the window. Wanted: Flyback's picture as a named live source in VJ software (Resolume, TouchDesigner, MadMapper, VDMX, OBS), where it is layered, put through effects and mapped onto projectors like any clip.

## The three protocols

| | Platform | How | License |
|---|---|---|---|
| Syphon | macOS | Shares the frame's GPU texture (OpenGL or Metal) with another app on the same Mac; no copy | BSD: bundle it, keep the notice |
| Spout | Windows | Shares a Direct3D 11 texture; from OpenGL through `WGL_NV_DX_interop` | BSD: bundle it, keep the notice |
| NDI | Everywhere | Compresses each frame and sends it over the network to any machine; a frame or two of latency | Proprietary, free: never in the repo or a release |

Syphon and Spout are a cable between two apps on one machine; NDI is a cable between machines. On Linux the local fallback is a `v4l2loopback` virtual camera.

## The shape

**Three plugins, one per protocol, sharing one new extension point: a picture output.** The native libraries stay out of the app, each plugin ships for the platforms it serves, and it matches the sound and MIDI backends. Store builds load no plugins, so VJs use the direct download.

The picture output, an addition to the plugin contract (a minor version):

- A plugin registers a named picture output. Flyback opens it with the picture's size when the user turns it on, calls it after every frame, and closes it when turned off.
- Each frame offers the GPU texture it was drawn in, tagged by kind (OpenGL, Metal, Direct3D), and a call that reads it back as pixels. An output says which texture kinds it takes; when the renderer has none of them it is handed pixels.
- Each frame carries its time on the patch's clock, so a receiver can line it up with the sound.
- A texture handle is valid only on the calling thread and for that one frame. The contract says so.

**NDI loads the user's own runtime, as ffmpeg is run (ADR-0089).** Flyback offers NDI output only when NDI's installer has put the runtime where it records it (`NDI_RUNTIME_DIR_V6` on Windows), loads it from there and nowhere on the search path, and says what is missing otherwise. Unlike ffmpeg it is a library in Flyback's process, so a version it cannot use is refused when loaded, not met as a crash.

**Sound over NDI uses the existing sound backend contract.** The NDI plugin also registers a sound backend named NDI; both feed one NDI sender, so the receiving machine gets picture and sound as one source, aligned by the frames' times. Choosing it sends the sound to the network rather than the speakers. Syphon and Spout carry no sound; local sound routing already works through driver cables (BlackHole, VB-Audio, PipeWire) picked as an ordinary sound device.

## Effort

The picture output's pixel half, `flyback-cli send --to <id> [--fast] [--json]` (its frames come from the same loop a clip is rendered by), Settings → Send and the NDI plugin's picture (`src/plugins/Flyback.Plugins.Ndi`, on the preset site) have landed. NDI has not met a real runtime: the check is NDI Tools on the Mac or the Windows machine, `flyback-cli send --preset Plasma --to ndi --seconds 60`, and the source seen in NDI Studio Monitor or Resolume. NDI's sound backend is about a day on the same sender. The texture half comes with Syphon or Spout: Spout 1 to 2 days (C wrapper; the GL/DX interop is where AMD and Intel drivers misbehave), Syphon 2 to 3 days (Objective-C, a small native piece built, signed and notarized on a Mac). Each needs a real check against Resolume's free demo on the Mac and the Windows machine, which CI cannot do.

## Open questions

- A Line In for `send`: the VJ's patch hears the room, and `send` has no microphone yet.
- A JACK sound backend (Linux, macOS) for app-to-app sound routing, as a separate item.
