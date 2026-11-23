# ADR-0240: A plugin can send the picture to another program

**Status:** Accepted · 2026-11-23 · *user-directed* · extends
[0025](0025-platform-io-behind-loadable-plugins.md)

## Context

A VJ uses Flyback as a visualizer for music played elsewhere and wants its
picture in Resolume, layered and mapped like any other clip. VJ software takes a
live picture from another program over Syphon on macOS, Spout on Windows and NDI
over the network. Each is a native library, Syphon and Spout under BSD licenses
and NDI under a proprietary one, and only VJs need any of them. The plan is in
`docs/handoff/vj-output.md`.

## Decision

**A picture output is a plugin's to offer**, through `IPluginRegistry.AddVideoOutput`,
the way a sound backend is. One plugin per protocol keeps the native libraries out
of the app and lets each ship for the platforms it serves; NDI's would load the
user's own runtime, as ffmpeg is run ([0089](0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md)).

**The contract carries pixels and the patch's time.** `IVideoOutput.Create` opens an
`IVideoFeed` for a `VideoFormat` (size and frame rate), and every frame drawn is
handed to `Send` as a `VideoFrame`: BGRA, top row first, with its stride and the
moment of the patch it shows. `VideoFrame` is a ref struct, so the pixels cannot
outlive the call. The time is what lines a picture up with a sound sent alongside,
which for NDI is a sound backend registered by the same plugin through the
existing contract. The contract moves to 1.1.0.

**`flyback-cli send` is how an output is checked**: it plays a patch into one with no
window, paced to the clock unless `--fast`, and says how many frames went. It is
hidden from the help, and the editor offers no switch, until a shipped plugin
offers an output.

## Consequences

- The GPU's own texture is not in the contract yet. Syphon and Spout share one with
  no copy; that is added, tagged by renderer, when the first of them is built, and
  a frame keeps its pixels for an output that takes no texture.
- Store builds load no plugins, so they cannot send the picture.

## Alternatives considered

- **Syphon and Spout built into the app.** Their licenses allow it, but every user
  would carry native code only VJs use.
- **A texture handle from the start.** It is the part that needs a real user to get
  right: which thread, for how long, and which renderer's kind.
