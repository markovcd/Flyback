<img src="docs/logo.svg" width="88" align="right" alt="">

# Flyback

Flyback is a patchable synthesiser for .NET 10. One graph can generate both a picture and a sound. The visual path and the audio path share the same module graph and are compiled down to the same flat instruction stream.



## Quick start

```bash
dotnet run --project src/Flyback.App -c Release
```

## Features

- Visual patch editor for building synth graphs by wiring modules together
- One patch generates both a picture and a sound from the same graph
- Per-pixel signal synthesis driven by coordinates and time
- Normalled sockets so common sources such as time and coordinates are present by default
- Feedback and iterative image generation via feedback modules and previous-frame sampling
- Sources, oscillators, patterns, forms, geometry, colour, maths, pitch, timing, shaping, time effects, feedback and measurement modules, with presets for each
- Live preview, output settings, recording and video export in the app shell
- MIDI input support through platform backends (Windows, macOS and Linux)
- CLI tools for rendering, checking, inspecting and bundling patches
- Plugin-based architecture for platform-specific audio/video backends and extensions
- Patch bundles that package the patch with its referenced sample and image files
- Agentic patch authoring through a model-backed assistant that can listen, propose changes and work inside the same patch graph
- Cross-platform publish targets for Windows, macOS and Linux

## Build and publish

```bash
dotnet publish src/Flyback.App -c Release -r win-x64 -o artifacts/win-x64
dotnet publish src/Flyback.Cli -c Release -r win-x64 -o artifacts/win-x64
```

This produces a self-contained folder with the app and CLI, plus the shared runtime and plugin folders:

```text
Flyback.exe          the app
flyback-cli.exe      the command line tool
Flyback.Core.dll     shared engine
Flyback.Plugins.dll  shared plugin host
plugins/             platform backends, and the Picture, Voice and Effects modules
```

Supported publish targets include:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`
- `linux-x64`

macOS bundles are published as `Flyback.app` beside the output folder.

## Docker builds

```bash
docker build --output artifacts .
```

This restores dependencies, compiles the solution, runs the tests, and publishes the self-contained outputs for the default target set.

To choose a specific set of runtimes:

```bash
docker build --build-arg RIDS="win-x64 win-arm64 osx-arm64 osx-x64 linux-x64" --output artifacts .
```

## CLI

The CLI runs the same engine without the Avalonia shell, so it is useful for rendering, checking and batch jobs.

```bash
flyback-cli render nebula.fbk -o nebula.png --size 1920x1080 --at 2.5
flyback-cli render drone.fbk -o drone.avi --seconds 30 --fps 30
flyback-cli render drone.fbk -o drone.wav --seconds 30
flyback-cli check nebula.fbk
flyback-cli info nebula.fbk
flyback-cli pack nebula.fbk -o nebula.fbkp
```

### Commands

- `render`: renders an image, movie or WAV from a patch
- `check`: compiles the patch and reports issues
- `info`: shows module and wire counts and compile cost
- `pack`: packs a patch together with the files it references

`check` exits with:

- `0`: no errors
- `1`: patch errors
- `2`: the job could not run

## Bundles

Flyback uses two file types:

- `.fbk`: a patch document
- `.fbkp`: a patch bundle containing the patch plus all referenced files

```bash
flyback-cli pack nebula.fbk -o nebula.fbkp
```

A bundle is a zip archive with the patch and any sample/picture files it uses. It is readable by standard tools and does not require unpacking to render.

## How it works

A patch is a graph, but during rendering it is compiled into a flat straight-line program over registers. Unused sections are not compiled, and the inner loop is designed to be cheap and predictable.

The project is split roughly as:

```text
src/
  Flyback.App       app shell and editor
  Flyback.Cli       command line tool
  Flyback.Core      engine and compiler
  Flyback.Plugins   plugin host and built-in plugin logic

tests/
  Flyback.Core.Tests      core engine tests
  Flyback.Core.Specs      specification-style tests and examples
  Flyback.App.Tests       app and UI tests
  Flyback.Plugins.Tests   plugin and runtime behavior tests
  Flyback.Plugins.OpenAi.Tests  OpenAI session tests
```

See the `docs/adr` folder for design notes and architecture decisions.

[`docs/language.md`](docs/language.md) specifies a text language that parses to
a patch — a second way to author one, proposed in
[ADR-0065](docs/adr/0065-a-text-language-that-parses-to-a-patch.md) and not yet
implemented.
