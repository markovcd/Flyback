<img src="docs/logo.svg" width="88" align="right" alt="">

# Flyback

[![Build](https://github.com/markovcd/Flyback/actions/workflows/ci.yml/badge.svg)](https://github.com/markovcd/Flyback/actions/workflows/ci.yml)

Flyback is a patchable synthesiser for .NET 10. One graph can generate both a picture and a sound. The visual path and the audio path share the same module graph and are compiled down to the same flat instruction stream.

The [website](https://markovcd.github.io/Flyback/) has screenshots, tutorials and the plugin guide. See [CHANGELOG.md](CHANGELOG.md) for what changed in each release, and the [releases page](https://github.com/markovcd/Flyback/releases) for downloads.

## The premise

Flyback is built strictly with AI. Every line of code, test, document and commit here is written by an agent; a human directs the work and decides what ships, but does not hand-write the source.

That is a constraint on authorship, not on engineering. The project holds to the same standards any modern codebase would: a CI gate that restores, compiles and runs the whole test suite on every push and pull request, signed releases built from the same image the gate stops inside, architecture decisions recorded in `docs/adr/`, and a changelog per release. Nothing merges that the gate has not passed.

Tokens are the one thing the project spends money on, and a donation goes on that budget. Flyback is strictly non-profit: nothing in it is sold, nothing is behind a payment, and nobody is paid out of it. The address is in About and in the site's footer.

[CONTRIBUTING.md](CONTRIBUTING.md) is how to work in it.

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
- Sources, oscillators, patterns, forms, geometry, color, maths, pitch, timing, shaping, time effects, feedback and measurement modules, with presets for each
- Live preview, output settings and live recording in the app shell; deterministic export through the CLI
- MP4, WebM, MOV, MP3, M4A and FLAC through ffmpeg where it is installed, found on `PATH` or picked by hand; Motion JPEG AVI and WAV are written by Flyback itself and need nothing
- MIDI input support through platform backends (Windows, macOS and Linux)
- CLI tools for rendering, checking, inspecting and bundling patches
- A text language a patch can be written in, saved as and read back from — the same instrument, as source
- A viewer that opens a patch and plays it at once, picture and sound, with no editor and nothing written
- Plugin-based architecture for platform-specific audio/video backends and extensions
- Patch bundles that package the patch with its referenced sample and image files
- Agentic patch authoring through a model-backed assistant that can listen, propose changes and work inside the same patch graph — over any chat-completions endpoint, or over Gemini, whose models hear the patch themselves
- Cross-platform publish targets for Windows, macOS and Linux
- Updates itself from signed GitHub releases: downloads a new release in the background and installs it at the next start (Settings → Updates to turn off)
- Counts how it is used — version, operating system, plugins and sound backend, the rough size of the machine, the kinds of module in a patch that plays, which assistant is asked, how long a run lasts and what it did, and where it crashed — anonymously, in coarse bands, and with nothing about any patch in it (Settings → Usage to turn off)

## Build and publish

```bash
dotnet publish src/Flyback.App -c Release -r win-x64 -o artifacts/win-x64
dotnet publish src/Flyback.Cli -c Release -r win-x64 -o artifacts/win-x64
dotnet publish src/Flyback.Viewer -c Release -r win-x64 -o artifacts/win-x64
```

This produces a self-contained folder with the app, the CLI and the viewer, plus the shared runtime and plugin folders:

```text
Flyback.exe          the app
flyback-cli.exe      the command line tool
flyback-viewer.exe   the viewer: opens a patch and plays it, and writes nothing
Flyback.Core.dll     the patch model and the module API, which plugins are built against
Flyback.Engine.dll   the compiler, the language and the renderers
Flyback.Plugins.dll  shared plugin host
plugins/             platform backends, and the Picture, Voice, Effects and Mastering modules
```

Supported publish targets include:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`
- `linux-x64`

macOS bundles are published as `Flyback.app` beside the output folder, whose `Info.plist` declares `.fbk`, `.fbkb` and `.fbks`. On Windows and Linux, Settings → Files registers the editor or the viewer to open them, for the current user.

## Docker builds

```bash
docker build --output artifacts .
```

This restores dependencies, compiles the solution, runs the tests, and publishes the self-contained outputs for the default target set.

To choose a specific set of runtimes:

```bash
docker build --build-arg RIDS="win-x64 win-arm64 osx-arm64 osx-x64 linux-x64" --output artifacts .
```

To restore, compile and run the whole test suite without publishing anything, which is what the Build workflow (`.github/workflows/ci.yml`) does on every push and pull request:

```bash
docker build --target gate .
```

## Releases and updates

The Release workflow (`.github/workflows/release.yml`) builds every platform, zips each one, and publishes them with a `SHA256SUMS` file and its signature, `SHA256SUMS.sig`. The app only installs an update when that signature checks out against the public key in `src/Flyback.App/Updates/release-key.pem`. The private key is kept in the repository secret `RELEASE_SIGNING_KEY`, and the workflow fails before it builds anything if the secret is missing or doesn't match the committed public key. It also fails before building if `CHANGELOG.md` has no `## X.Y.Z` heading for the version being released, since that heading is what the "What's new" window reads. [ADR-0088](docs/adr/0088-a-release-installs-itself-at-the-next-start.md) explains the design. The same key signs the Figures plugin package, which the workflow attaches to the release and the preset site starts with ([ADR-0141](docs/adr/0141-the-preset-site-starts-with-a-plugin-its-build-packs-and-the-release-key-signs.md)).

Everything but the publishing is `release.sh`, which runs the same here:

```bash
./release.sh 1.4.0
```

It builds, tests, zips, packs and signs into `dist/`, and publishes nothing. Off GitHub, `RELEASE_SIGNING_KEY` holds a local test key rather than the release key: `release-key.sh` takes it from the environment, or makes one and keeps it in the user environment, and only GitHub stops at a key that does not pair with the committed public key or at a missing changelog heading. The preset site's build and a Release run of the site sign with the same variable, and a Debug build checks no keys at all. A build on this machine, `make.sh` and `release.sh` included, embeds the public half of the local key in place of `release-key.pem`, so a local release installs over local builds; on GitHub the committed key is embedded.

To make the key pair, with the `openssl` that comes with Git Bash:

```bash
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out flyback-release.key
```

```bash
openssl pkey -in flyback-release.key -pubout -out src/Flyback.App/Updates/release-key.pem
```

Commit the public key. Paste the whole contents of `flyback-release.key` into the `RELEASE_SIGNING_KEY` secret, keep a copy somewhere safe such as a password manager, and then delete the file. Never commit it. Installed copies only accept releases signed with the key they were built with, so a lost key means everyone installs the next release by hand.

## CLI

The CLI runs the same engine without the Avalonia shell, so it is useful for rendering, checking and batch jobs.

```bash
flyback-cli render nebula.fbk -o nebula.png --size 1920x1080 --at 2.5
flyback-cli render drone.fbk -o drone.mp4 --seconds 30 --fps 30
flyback-cli render drone.fbk -o drone.avi --seconds 30 --fps 30
flyback-cli render drone.fbk -o drone.mp3 --seconds 30
flyback-cli render drone.fbk -o drone.wav --seconds 30
flyback-cli render drone.fbk -o drone.wav --seconds 30 --loudness
flyback-cli render drone.fbk -o drone.mkv --seconds 30 --format mp4 --ffmpeg /opt/bin/ffmpeg
flyback-cli check nebula.fbk
flyback-cli check nebula.fbk --strict
flyback-cli compare nebula.fbk nebula-ported.fbk --seconds 30
flyback-cli info nebula.fbk
flyback-cli modules
flyback-cli pack nebula.fbk -o nebula.fbkb
flyback-cli pack-plugin Flyback.Plugins.Ripple.csproj -o ripple.fbkp --key ripple.key
flyback-cli plugin-key -o ripple.key
flyback-cli print nebula.fbk -o nebula.fbks
flyback-cli print nebula.fbk --check
flyback-cli render nebula.fbks -o nebula.png
flyback-cli probe --keys
flyback-cli probe --provider all
flyback-cli viewer nebula.fbk
```

### Commands

- `render`: renders a still, a clip or a sound file from a patch. The extension picks the format — `.png`, `.avi`, `.mp4`, `.webm`, `.mov`, `.wav`, `.mp3`, `.m4a`, `.flac` — and everything but `.png`, `.avi` and `.wav` is encoded by ffmpeg, taken from `PATH` unless `--ffmpeg` names one. `--format` overrides the extension, and `--loudness` prints how loud the sound came out: integrated loudness in LUFS and true peak in dBTP, measured as ITU-R BS.1770 does. The patch runs compiled; `--interpreted` keeps it on the interpreter, which writes the same bytes more slowly.
- `check`: compiles the patch and reports issues
- `info`: shows module and wire counts and compile cost
- `pack`: packs a patch together with the files it references
- `pack-plugin`: builds a plugin into a `.fbkp`, signed with the key `--key` names
- `plugin-key`: makes the key a plugin's packages are signed with, which every update must be signed with too
- `viewer`: starts `flyback-viewer` with everything after the word, so `flyback-cli viewer --help` is the viewer's own help
- `print`: writes the patch out as text in the language, and can check that the text builds back to the same program
- `compare`: plays two patches side by side for `--seconds` at `--size` and says whether they are the same instrument, sample for sample and pixel for pixel, and where they first part when they are not; it exits `1` when they differ
- `modules`: lists the modules this build has, and which plugin defines each
- `probe`: asks an assistant which models it has and what each one accepts

`check` exits with:

- `0`: no errors
- `1`: patch errors
- `2`: the job could not run

`--strict` makes a warning fail as well. `check`, `compare`, `info`, `pack`, `modules` and
`probe` each take `--json`, which writes the same answer as a document instead of
as prose.

`info` says what a patch requires and `modules` says what is installed to meet
it, which is the pair to reach for when a patch reports that it did not load
completely.

`probe` is the one command that reaches off the machine. It asks a provider's endpoint what it
offers and records the answer in the settings file both programs read, so the app's model box
fills itself in without being told. It takes minutes and the provider bills for it, so it says
what that means and waits for a yes; `--yes` answers for a script, which has nobody to ask.
`--keys` says where each key would come from and asks nothing of anybody, and `--dry-run` prints
what was found and leaves the settings alone.

Settings → Assistant runs the same survey from a button, about the one model the form is set to.
It goes out with the provider, the form and the key as they stand on the settings window rather
than with what was last saved, which is how a model, a key or an endpoint can be tried before
any of them is kept. What it finds is written over that model's line in the list this command
left; filling the list is the command's job, since only it asks about every model.

### Completion

The CLI answers the `[suggest]` directive, which is the protocol `dotnet-suggest`
speaks:

```bash
flyback-cli "[suggest:13]" "flyback-cli pr"   # print, probe
```

So installing that tool and adding its shim to your shell gives completion of
commands, options and file arguments.

## Bundles

Flyback uses three file types:

- `.fbk`: a patch document
- `.fbkb`: a patch bundle containing the patch plus all referenced files
- `.fbks`: the patch written as text, in the language

```bash
flyback-cli pack nebula.fbk -o nebula.fbkb
```

A bundle is a zip archive with the patch and any sample/picture files it uses. It is readable by standard tools and does not require unpacking to render.

The app saves and opens all three. A `.fbks` is a copy rather than a document: it keeps the instrument exactly — the text builds back to the same program, op for op — but not the groups or the canvas layout, so saving one leaves the open document as it was.

## Viewer

`flyback-viewer` opens a patch and plays it, picture and sound, with no editor around it. It writes nothing — no settings, layout, recovery file or statistics — and takes its defaults from the same `output.json` the editor saves, every one of them overridable on the command line.

```bash
flyback-viewer nebula.fbk
flyback-viewer --preset "Dub" --size 1080p --mute
flyback-viewer drone.fbk --from 30 --cpu --for 10
flyback-viewer --preset "Whole band" --hidden --for 10
flyback-viewer --help
```

Hover the bottom right corner of the window for the sound, rewind and pause buttons; double-click the picture for full screen. A patch with no picture, or a run with `--no-video`, opens as just those buttons and its knobs. Space, or Ctrl+P, pauses. `--background` opens the window without taking focus, and `--hidden` opens none at all.

A patch made to be played is played here too: the computer's keys are notes wherever the patch reads them, a MIDI In hears the device it names, and a panel knob bound to a MIDI controller follows it. There is no knob panel, so a knob with no controller stays where the patch left it.

## How it works

A patch is a graph, but during rendering it is compiled into a flat straight-line program over registers. Unused sections are not compiled, and the inner loop is designed to be cheap and predictable.

The project is split roughly as:

```text
src/
  Flyback.App       app shell and editor
  Flyback.Cli       command line tool
  Flyback.Core      patch model, module API and the built-in modules
  Flyback.Engine    compiler, text language, renderers and file formats
  Flyback.Plugins   plugin host and built-in plugin logic
  Flyback.Ui        the preview, sound device and look the app and the viewer share
  Flyback.Viewer    the viewer: opens a patch and plays it

tests/
  Flyback.Core.Tests      core engine tests
  Flyback.Core.Specs      specification-style tests and examples
  Flyback.Core.Benchmarks engine benchmarks
  Flyback.App.Tests       app and UI tests
  Flyback.Cli.Tests       command line tests
  Flyback.Plugins.Tests   plugin and runtime behavior tests
  Flyback.Plugins.OpenAi.Tests  chat-completions session tests
  Flyback.Plugins.Gemini.Tests  generateContent session tests
```

[`docs/engineering-guide.md`](docs/engineering-guide.md) is how the codebase is put together, how its code is written and how its tests are written. See the `docs/adr` folder for the architecture decisions behind it.

[`docs/language.md`](docs/language.md) is the reference for the text language — a
second way to author a patch, decided in
[ADR-0065](docs/adr/0065-a-text-language-that-parses-to-a-patch.md). The app
saves and opens it, the CLI reads it wherever it reads a patch and writes one with `print`, and the
assistant writes a whole patch in one call with it.
