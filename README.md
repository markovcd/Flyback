<img src="docs/logo.svg" width="88" align="right" alt="">

# Flyback

A patchable synthesiser for .NET 10 that makes a picture and a sound out of one
patch. Images are generated the way an analogue synth generates sound:
oscillators, maths and feedback, evaluated per pixel. Nothing is drawn — every
frame is a function of `(x, y, t)`, and every sample is that same function with
only `t` moving.

The mark is a sawtooth: the ramp that sweeps a CRT line and the fast retrace
back to start the next one — the flyback the program is named after, and one of
its own oscillators besides.

```
Coordinates ──▶ Sine ──┐
                        ├──▶ Add ──▶ Remap ──▶ HSV ──▶ Output
Coordinates ──▶ Sine ──┘
```

## Running it

```bash
dotnet run --project src/Flyback.App -c Release
```

## Publishing

Both programs go into one folder — the window, and the command line described
below:

```bash
dotnet publish src/Flyback.App -c Release -r win-x64 -o artifacts/win-x64
dotnet publish src/Flyback.Cli -c Release -r win-x64 -o artifacts/win-x64
```

```
Flyback.exe          the shell
flyback-cli.exe      the command line
Flyback.Core.dll     ┐ the plugin boundary, and with it everything the two share:
Flyback.Plugins.dll  ┘ one engine, one plugin host, one runtime
plugins/             one folder per plugin, read by both
```

They are self-contained, so the machine they land on needs no .NET installed.
They are deliberately *not* single files, and sharing the folder is why: two
shells over one engine have almost every byte in common, and two bundles would
carry two copies of it — 109 MB together this way against 172 MB apart. Symbols
are not published either; they are in the build output, where a debugger looks
for them.

Run from a terminal, both behave the way a program should: the shell waits, and
anything written — an unhandled exception, whatever Avalonia complains about —
comes back on that terminal rather than nowhere. On Windows that takes a console
subsystem, which also means a console of its own when the window is started from
Explorer instead; it hands that one back at startup, so what you see is a console
that flickers and closes rather than one that stays.

`Flyback.Core.dll` and `Flyback.Plugins.dll` are the pair `PluginLoadContext`
calls host-owned — what a plugin is written against — and their being on disk is
what lets anyone build a plugin against the exact version a build shipped.

`win-x64`, `win-arm64`, `osx-arm64`, `osx-x64` and `linux-x64` all work, from any
of them — the engine and the shell are portable, and the parts that are not are
plugins. Each build gets the plugins for the platform it is *for*, not the one it
was made on: a macOS publish contains CoreAudio and CoreMIDI and no NAudio, a
Linux one ALSA's sound and ALSA's MIDI and neither, and a Windows one WASAPI,
data protection and winmm's MIDI — each build carrying its own platform's plugins
and none of anybody else's.

Anything cross-published from Windows onto a Unix arrives without an executable
bit, because the filesystem writing it has no concept of one — `chmod +x Flyback`
once, at the other end.

macOS runs a bundle rather than a folder, so publishing for an `osx-*` identifier
also lays out `Flyback.app` beside the publish output:

```bash
dotnet publish src/Flyback.App -c Release -r osx-arm64 -o artifacts/osx-arm64/publish
# → artifacts/osx-arm64/Flyback.app
```

A bundle cross-published from Windows needs one more thing besides that mode: the
signature macOS on Apple silicon demands before it will run anything at all.
Once, on the Mac:

```bash
chmod +x Flyback.app/Contents/MacOS/Flyback && codesign --force --deep --sign - Flyback.app
```

Publishing on macOS itself sets the mode, and leaves only the signature.

## Building with Docker

The whole release without a .NET SDK on the machine: restore, compile, every
test in the solution, and one self-contained publish per platform.

```bash
docker build --output artifacts .
```

That leaves `artifacts/win-x64` and `artifacts/linux-x64`, each holding both
programs exactly as above, and `artifacts/osx-arm64`, where the same folder is
inside `Flyback.app` — the bundle holds the whole payload, so nothing is left
beside it. The tests are a step in the build rather than something you run
afterwards, so a red one fails the `docker build` and nothing is written.

The other two identifiers are an argument rather than an edit, and each is a
whole copy of the runtime, so ask for what you need:

```bash
docker build --build-arg RIDS="win-x64 win-arm64 osx-arm64 osx-x64 linux-x64" --output artifacts .
```

Building on Linux is also how the executables get their mode — the thing a
cross-publish from Windows cannot do. `--output artifacts` onto an NTFS drive
loses it again, so to carry a Unix build to a Unix machine, take the tar:

```bash
docker build --output type=tar,dest=flyback.tar .
```

The macOS signature is still the one thing that has to happen on a Mac.

Leave `--output` off and the build is a pure check — everything compiles,
everything passes — which is what it is worth running in CI. `--output` writes
into the directory rather than replacing it, so delete `artifacts` first if you
want to be sure nothing from a previous build is still lying there.

## From the command line

`flyback-cli` is a second shell over the same engine, published into the same
folder as the window. It carries no Avalonia, and therefore needs no display, no
fonts and no X libraries on the machine that runs it: a patch renders on a build
server the same way it renders on a desk.

```bash
flyback-cli render nebula.fbk -o nebula.png --size 1920x1080 --at 2.5
flyback-cli render drone.fbk  -o drone.avi  --seconds 30 --fps 30
flyback-cli render drone.fbk  -o drone.wav  --seconds 30
flyback-cli check nebula.fbk
flyback-cli info  nebula.fbk
flyback-cli pack  nebula.fbk -o nebula.fbkp
```

The extension decides what `render` writes — a still of one moment, a clip of
both halves, or the sound alone. It always renders on the interpreter rather
than the shader backend, which needs a context and a window; that is also what
makes it repeatable, since the two backends are allowed to differ in their last
bits ([ADR-0035](docs/adr/0035-a-glsl-backend-for-the-video-path.md)). The same
patch at the same moment is the same bytes.

`check` compiles both sinks and reports what the compiler has to say. It is the
one worth putting in CI, because the exit code carries the answer: **0** when
there is nothing wrong, **1** when the patch has errors in it — warnings do not
fail, since a patch that only warns is one somebody may have meant — and **2**
when the job could not be done at all, a file missing or a size that is not one.
`render` refuses a patch with errors rather than writing a file made of
stand-ins.

`info` says what a patch is made of and what each half of it costs, which is the
gap that dead-code elimination buys:

```
nebula.fbk
  modules   19
  wires     29
  requires  flyback
  picture   80 ops, 84 registers
  sound     6 ops, 6 registers, nothing wired in
```

`check` and `info` both take `--json`. Everything a command produces goes to
stdout and everything it says about the work goes to stderr, so a redirect
catches the one without the other.

Every command takes a **bundle** wherever it takes a patch, and a bundle carries
its own files — so this works in a folder holding nothing else at all:

```bash
flyback-cli render nebula.fbkp -o frame.png
```

Nothing is unpacked to do it. The archive is read into memory and the patch is
compiled against what was read beside it, so a build server that has never seen
the photographs and recordings a patch names still draws it, and writes nothing
but the frame it was asked for.

## Bundles

A `.fbk` is a document full of paths, and paths mean something only on the
machine that wrote them. A `.fbkp` is that document with everything it names
travelling beside it:

```bash
flyback-cli pack nebula.fbk -o nebula.fbkp
```

```
nebula.fbkp
  carried  2 files
    C:\sounds\drums.wav
    D:\pictures\moon.png
```

**It is an ordinary zip**, which is most of the argument for it. The format is in
the framework, so it adds no dependency
([ADR-0019](docs/adr/0019-no-third-party-dependencies-in-the-engine.md)); every
operating system already opens one, so a bundle this program some day cannot read
is still a folder somebody can get their work out of; and the zip's own directory
is the manifest, so there is nothing to keep in step.

```
patch.fbk
files/drums.wav
files/moon.png
```

The patch inside is not quite the patch outside: every path it holds is rewritten
to name the copy in the archive. That is the whole trick, because a relative path
is already measured from wherever the patch is — so **an unpacked bundle is a
working patch with nothing else arranged**, whether it was unpacked by this
program or by double-clicking it in a file manager. There is no `unpack` command
for that reason.

The window offers a bundle beside a patch in both pickers, and **a bundle is a
document rather than a copy of one**: saving it marks the patch saved, takes the
name in the title bar, and answers the question about unsaved changes. The two
kinds of file differ in what is in them and in nothing else.

Opening one writes nothing anywhere. The archive is held as it came and the
folder stays behind it, so a module pointed at a file on this machine a moment
later means the file on this machine. Saving it again writes the same bytes back
— a photograph is not re-encoded on the way through, which would quietly make a
sixteen-bit file an eight-bit one.

That costs one copy of the compressed bytes for as long as the bundle is open,
and costs the undo history nothing: the history is snapshots of the patch, the
patch is paths, and payloads sit beside the document exactly as the caches
already do. Which is the whole of what
[ADR-0052](docs/adr/0052-a-patch-names-its-samples-rather-than-carrying-them.md)
was about, untouched.

Saving a bundle as a loose `.fbk` writes what it was carrying beside it, under
the names the patch already uses — the inverse of packing, and the one place
saving writes more than the file it was given. Nothing already there is
overwritten.

A file that has gone is reported and the bundle is written anyway, which is the
same call `check` makes: the patch opens, compiles and draws without it. What is
missing is the claim to be self-contained, and `pack` exits **1** to say so.

What goes in is asked of the modules rather than listed here — a kind of carried
state that names a file says so, so a third kind of file is carried without the
packer being touched.

## How it works

A patch is a graph, but the graph is never walked while rendering. It compiles
to a flat, straight-line program over `float` registers:

```
Patch (nodes + wires)
  └─▶ PatchCompiler      walks back from Output, topologically
       └─▶ Op[]          e.g. r7 = Mul(r4, r6)
            └─▶ SynthRenderer   evaluated once per pixel, rows in parallel
```

This matters for three reasons: the inner loop has no virtual dispatch and no
allocation; anything the Output node can't reach is never compiled, so unused
modules cost nothing; and because each op is one line of arithmetic, the same
op list is what a future GLSL backend would emit rather than interpret.

Rendering a frame is the expensive thing here, not making sound. On a 12-core
desktop a 960×540 frame of the Plasma preset costs about 23 ms of wall time and
127 ms of CPU across the rows, so the preview settles below 60 Hz on anything
elaborate — while a second of audio for the same patch costs around 6% of one
core.

That asymmetry is why the renderer leaves one core alone and why the preview
rests between frames in proportion to what the last one cost. Sound has a
deadline and a picture does not: a dropped frame is invisible, and a late audio
buffer is a click.

### Coordinates

`y` runs −1 (bottom) to 1 (top). `x` is the same scale widened by the aspect
ratio, so a circle stays a circle. `t` is seconds since the patch started.

### Wires you do not have to draw

Two wires used to be in almost every patch, and they were the same two every
time: **Time** into an oscillator's or a sequencer's `in`, and **Coordinates**
into the `x` and `y` of anything that reads a position. Neither was a decision.
An oscillator without the first holds one value — silence, or a flat field — and
a Rings without the second reads the single point at the middle of the screen.

Those sockets are now *normalled*
([ADR-0050](docs/adr/0050-normalled-sockets-carry-a-signal-with-no-wire.md)),
which is the rack's word for a jack that is already carrying something before you
plug into it. Place an oscillator and it is oscillating; place a Rotate and it is
turning the picture. Patching the socket overrides it and unplugging brings it
back, exactly as a wire overrides a knob.

The module behind a normal is hidden and shared: there is no Time on the canvas
and no wire from it, one hidden Time serves every socket normalled to it, and it
is loaded once per program however many oscillators read it. A normalled socket
has no knob — nothing would read the value — so the node names what is driving it
where the number would be, and the inspector says why there is no wire. To hold
something still on purpose, patch a **Value** in; the constant then reads off the
patch rather than off a socket nobody looks at.

What is *not* normalled is anything whose common source is Time rather than its
only sensible one — Noise's `z`, Rings' `offset`, an angle to turn a Rotate by.
A still noise field is a thing worth being able to have.

The presets are written this way, which took 18 modules and 55 wires out of the
ones that ship without changing one op of any of them. What is left is the
wire that is a decision: Plasma keeps both its Coordinates wires because a sine
across `x` and another across `y` *is* the patch, Chromatic keeps a Coordinates
for `radius`, and Drone keeps a Time for the Rings' `offset`. Every source module
still in a preset is there because that patch needed a source.

### Feedback

The renderer keeps the previous frame in linear float RGB, and the `Feedback`
module samples it. Route it back towards Output through `Rotate` or `Scale` and
you get the camera-pointed-at-its-own-monitor tunnel — the "Feedback tunnel"
preset does exactly that. Values are clamped to 0..1 each frame, which is what
stops the loop running away.

### Nebula

The preset worth reading the patch of. Coordinates are turned, folded into eight
wedges, bent by a noise field, taken as travelling rings, and laid over a
dimmed, slowly rotating copy of the previous frame.

The order of the fold and the bend is the whole trick, and it is not the obvious
one. Warping *before* folding looks lovely and is not a kaleidoscope at all: the
displacement varies per pixel, so the fold has nothing symmetric left to work
with and the eight-fold structure disappears. Folding first and warping inside
the wedge — by a field read from the folded plane, so it repeats with it — keeps
the symmetry and bends it at the same time.

At 79 ops it is the most expensive patch here, which is the other reason to keep
it: it is what the renderer looks like under load.

### Looking at a value

Patch an output into a **Probe** and select it, and the screen shows a chart of
that value instead of the picture — the future half of it as readily as the past,
which is what a machine that is a pure function of `t` can do and a scope cannot.
Its own panel explains the rest. The sound is never interrupted while you look,
because the speakers root at the Output whatever the screen has been asked for.

Nothing is measured and nothing is read back: a chart of a signal is itself a
function of `(x, y, t)`, so the probe is an ordinary module compiling to ordinary
ops, and the GPU backend draws it without being told it exists
([ADR-0040](docs/adr/0040-a-probe-is-a-second-compile-root.md)). What makes it
work is that its input is read over a *different* domain from the pixel drawing
it — time swept across the picture, x and y pinned to the middle — so what you
get is the signal at the centre of the screen over a couple of seconds, not the
field it makes.

### Looking at what was played

A Probe recomputes the signal at every column, which is what lets it draw the
future — and also why it draws an oscillator without its accumulated phase and a
delay line as a wire. The video path has no memory, and the Probe does not
pretend otherwise.

A **Scope** is the other half of that. It computes nothing: while sound is
playing, the speakers' program hands it one evaluation at a time, in order, and
it keeps the last stretch. Select it and you see what was actually heard —
including everything the Probe structurally cannot show, an envelope that was
triggered, a sample playing, a delay tail, a filter settling.

The window is stretched across the whole frame, with `now` at the right-hand
edge, and the trace fades into the past behind it the way a slow tube does — so
which end is the moment is something you see rather than something you have to be
told. The graticule is eight divisions across whatever the frame turns out to be,
so a square stays an eighth of `window` across and a quarter of `scale` up.

The cost is three cliffs that are really one. It shows nothing until sound is
switched on; it shows nothing the Output's `left` and `right` do not reach, since
a branch that only draws was never played; and it shows only the past. Put a
Probe and a Scope on the same node and the two charts differ wherever the patch
has memory in it. That disagreement is the point of having both, and is why the
Scope is a separate module rather than a switch on the Probe.

Mechanically it is the only module whose input is a *root* of a program: nothing
downstream reads a Scope, so the walk from the speakers would never reach what it
is looking at, and the compiler roots at every tap as well as at the sink. That
is the per-sink dead-code elimination given up on purpose, for the one thing that
cannot work without it
([ADR-0053](docs/adr/0053-a-scope-records-what-the-speakers-played.md)).

### Drawing with what was played

A Scope is a picture *of* the sound. A **Meter** is a number *about* it, and it is
the one thing here that lets a patch stop saying itself twice.

Everything before it that made the eye and the ear agree did it by sending one
modulator to both — the Drone's sweep, the Sequence's gate. The Kick preset says
what that costs: its envelopes have no memory on the video path, so what the
screen gets of a drum is the gate, a flash a beat, in time with the sound rather
than shaped like it. Patch the drum into a Meter instead and the picture has the
envelope, the fall of it included, because what is driving the picture is a
reading of what the speakers actually played. The **Heard** preset is exactly
that patch.

It taps its input the way a Scope does, and everything after that is the opposite
of one. `level` is the loudness of the last `window` seconds — the steady one —
and `peak` is the furthest that stretch got from silence, which is the one that
hits; both come off one pass, because the difference between them is most of what
you want. `window` is the whole of the smoothing: a few milliseconds follows every
drum, half a second leans into the music.

Two things make it cheap, and they are the reason it is a separate module rather
than a mode on the Scope. Its input is swept, so the signal it is listening to is
never lowered into the picture's program — a hue that follows a bass line does not
compute a bass line per pixel, it reads one number. And that number arrives as a
**live input**, the same way a note somebody is holding down does, so it lowers to
a uniform and the preview keeps the GPU. A Scope cannot: a chart is a table read,
and a table is the one thing the shader cannot draw.

Which is the part worth knowing about how it works. There is no arithmetic in the
module and no opcode behind it — the picture does not work out how loud the sound
is, and nothing in a program could, since a frame is one evaluation per pixel with
no past to reduce. Something outside both programs listens to the same ring the
Scope charts, once a frame, and plays the answer in
([ADR-0058](docs/adr/0058-the-picture-is-told-how-loud-the-sound-is.md)).

So a Meter reads nothing where nothing is playing: a still has no past to be loud
in, and with the sound switched off every one of them is zero — which is worth a
floor under whatever it drives, or the patch draws black and looks broken. An
export is the exception, and measures itself: `render` to an AVI writes each
frame's audio before the frame, so a clip is lit by the sound it is played with.

## Sound

The same modules drive the speakers.

Nothing about the catalogue changes: audio is the same machine with only `t`
varying and a scalar coming out instead of a colour. Compilation is rooted at a
sink, so one patch produces one program per sink — and each pays only for the
modules it actually reaches. A noise field feeding the screen costs the speakers
nothing.

Screen and speakers are two halves of one block rather than two modules, and
every patch has exactly one of it: it is not in the palette, it cannot be
deleted, and everything about seeing or hearing the patch lives in its panel.
What each half costs is still separate — the picture's program walks back from
`colour` alone and the sound's from `left` and `right`, so neither pays for the
other.

The audio side of the catalogue is **Frequency** and **Note** for pitch, **Note
Sequencer** and **Sequencer** for a tune, **Tempo** for saying how fast in beats
a minute rather than in beats a second, **ADSR** for the shape a note has over
time, **Mixer** for summing, **MIDI In** for playing the patch by hand rather
than programming it, and the Output's own `scan` for hearing the picture instead
of the clock. Each explains itself on its panel. Two details are on none
of them: a Note's `octave` transposes by twelve semitones a step, and its
`cents` detunes past the snap.

The **ADSR** is the one module here with a memory that is not a delay line. It
holds its level and one latch in a pair of the one-evaluation cells ADR-0041
gave the filter, so it needed no opcode of its own — and like everything else
with a memory it is audio only. A picture is one evaluation with nothing before
it, so there is no time for a shape to happen in and what comes out is the gate
at its sustain level.

A **Mixer**'s levels are sockets like everything else here, which is the part
worth saying twice — a fader is something an oscillator can sweep as well as
something a hand can set, and because its sockets are untyped the same module
mixes four pictures as readily as four tones.

The **Drone** preset is the demonstration: one slow oscillator sets both the hue
of the image and the tremolo on the tone, so the two sinks are visibly and
audibly the same signal.

**Chromatic** is the same idea through a **Note**. One ramp is snapped to whole
notes and sent to both sinks, so what the ear hears as a run of separate notes
the eye sees as flat rings of colour — and because the audio path pins `x` and
`y` to zero, the module the speakers hear one note from is showing the screen the
fourteen either side of it. Brightness is taken from the ramp before the snap, so
a smooth glow and the rings sitting in it are the before and after of the same
signal.

**Sequence** is a tune rather than a scale. Eight notes in a list go to the
speakers, and the sequencer's other two outputs go to the screen: `index` picks
the hue and the number of rings, so the picture reorganises itself on the beat,
and `gate` dims it exactly where it silences the note, so the rhythm is visible.
Nothing is duplicated between the two sinks — every difference between what the
ear gets and what the eye gets is a different output of the one module.

Its gate ramps rather than switches, which is not a nicety. ADR-0030 stopped a
stepped *pitch* clicking; a stepped *amplitude* is the same failure from the
same cause, and nothing was catching it — the first version of this preset tore
its waveform at nineteen times the wave's own travel, six times a second.
Oversampling band-limits a discontinuity rather than removing one, so the fix is
an envelope: four ops, no state, and the edges are a fraction of a step so they
scale with the tempo.

A sequencer needs no memory, which is why it is one of the few things here that
behaves identically at both sinks. Which step is playing is a function of where
its `in` has got to, not of what played before, so unlike a delay (ADR-0027) or
an accumulated phase (ADR-0030) there is nothing to carry and nothing a
recompile can disturb. `in` is a domain rather than a clock for the same reason
an oscillator's is: stop it and the sequence stops, run it backwards and it runs
backwards.

The notes themselves are a list on the module rather than knobs on it, which is
what lets a pattern be any length and a note any duration
([ADR-0038](docs/adr/0038-a-sequencers-notes-are-a-list-on-the-node.md)). That
list is the one thing here a module carries that is not a knob, and it costs the
one thing the old row of sockets could do: a step is no longer something you can
patch into. Where every note is the same length the module compiles to exactly
the ops it always did; only an uneven pattern pays for being uneven.

**Four voices** is the **Mixer** wired up, and it is laid out as four channel
strips because that is what it is. Each row is one voice: a note and a sine at it
for the ear, the same oscillator read across the radius instead of across the
clock for the eye, and one slow sine setting both of their levels. The faders are
the only thing the two sinks share — level three on the chord is level three on
the screen — so what fades up in the sound is visibly the thing that fades up in
the picture.

There are two Mixers in it and only one module: the chord sums four scalars, the
picture sums four colours, and both do it with the same four multiplies and three
adds, because the sockets are untyped like the maths modules'. What differs is
what each sink does with a sum that overflows. The Output's gain is a quarter,
which is exactly four voices at full and puts the worst case at full scale rather
than past it; the picture's Gain is far more generous, since light that runs over
clips to white and reads as brightness, while sound that runs over reads as a
fault.

**Kick** is a drum, and the one preset here whose parts are all times. A **Pulse**
at two hertz — 120 beats a minute, off a **Tempo** knob that says so in those
words — triggers two **ADSR**s at once. One shapes how loud the note is over three
hundred milliseconds; the other shapes what pitch it is over forty, and that
second one is the whole difference between a drum and a beep: the note starts
around two hundred hertz and has fallen to forty-five before the first is half
gone. What the ear hears at the top is the beater and what it hears after is the
shell, and both are one oscillator.

The eye gets a disc struck on the beat and fading out before the next one — and
what lights it is a **Saw** at the same tempo rather than the level envelope.
An envelope has no memory on the video path, so all it can hand the screen is its
gate, and a gate is open for all but a fiftieth of each beat: a disc lit by one is
not a drum being struck but a lamp that is on, with a ten-millisecond hole in it.
That hole is shorter than a video frame, so whether any frame lands in it is
luck, and the disc appears to blink at whatever rate the two beat against each
other. A saw is the shape the envelope would be if it could run, and being a pure
function of time it needs no memory to do it.

**Heard** is that same drum with the picture listening to it instead — a Meter on
the voice, its `peak` lighting the rings and its `level` taking the hue. Nothing
is sent to both sinks: what the eye gets is a reading of what the ear was actually
played, so the flash has the envelope's shape rather than the trigger's, and the
colour leans after the hit the way a room does. Switch the sound off and it stops
moving, which is the honest thing a Meter has to say
([ADR-0058](docs/adr/0058-the-picture-is-told-how-loud-the-sound-is.md)).

### Why a stepped pitch does not click

It very much used to. An oscillator's phase was `in × freq`, which is the natural
shape for a machine that is otherwise a pure function of `(x, y, t)` — a picture
has no previous sample to carry a phase in (ADR-0005, ADR-0006). But phase is the
integral of frequency, not its product with time, and the two only agree while
`freq` holds still. Step `freq` at time `t` and the product jumps by `t` times the
change: ten seconds in, one semitone at A3 skips about 131 whole cycles and the
wave restarts wherever that lands. The tear is the click, and it grows with the
clock.

Smoothing the pitch cannot fix that, and trying is instructive. The pitch actually
produced is `freq + t × freq′`, so a ramp across each step trades an infinite
`freq′` for a finite one multiplied by `t` — a wobble of nearly 2 kHz on a 220 Hz
tone, ten seconds in. A *narrow* ramp is worse than no ramp: the same excursion,
held longer. Only a ramp wide enough to be a portamento sounds clean, at which
point the quantiser is not one.

So the oscillators accumulate instead (ADR-0030). Each keeps a running phase and
advances it by however far its `in` moved, in cycles of `freq` as it is *now*. A
frequency that jumps moves the phase by one ordinary step regardless, so the
wave's value carries straight across the change and only its slope differs — and
a slope has no click in it. Nothing about the pitch is smoothed. Measured on
Chromatic as the largest sample-to-sample jump against the median one, over a
second of raw program output:

| measured from | multiplied | accumulated |
|---|---|---|
| 0 s | 237× | 1.6× |
| 20 s | 538× | 1.8× |
| 60 s | 648× | 2.6× |

`in` stays the socket it was: the step is measured from the input rather than
counted off a clock, so a domain that stops stops the tone and one running at
twice the rate doubles the pitch, exactly as the multiply did. Drawn rather than
heard, the accumulator *is* the multiply — one evaluation per pixel has nothing
to accumulate — so an oscillator's picture is unchanged to the byte.

### Playing in a key

`Note` pulls a signal onto the nearest semitone, and a **Quantiser** pulls it
onto the nearest note of a scale — so a sweep becomes a run up that scale and a
noise field becomes a tune in it. Its twelve switches are pitch classes: turning
`A` on puts every A in the scale rather than one of them, which is what makes a
scale repeat up the keyboard.

Its `hold` freezes the note for as long as it is up, which is the socket every
quantiser in a rack has. It is a level rather than a trigger, and that is what
lets it be optional: nought is down, an unpatched socket is nought, and one
nobody has wired anything into snaps continuously as it always did. A level also
states the guarantee the right way round — an edge says when the note *may*
change, and what anybody wants is that it *cannot* while a note is sounding.
Wire the gate that opens the envelope and the two are the same interval by
construction. A **Sample & Hold** does the same job for any other signal, and
holds it the same way.

They are a set on the module rather than twelve sockets, for the reason a
sequencer's notes are a list on the module
([ADR-0038](docs/adr/0038-a-sequencers-notes-are-a-list-on-the-node.md)): which
notes exist is a decision about the piece, not a signal in it, and twelve inputs
would be a module nobody could read. So they are edited as the octave they are a
subset of — an actual keyboard in the panel, since C major is a picture before it
is a list of numbers.

What is switched on decides what the module *compiles to* and not only what it
computes. Each note in the scale contributes one candidate — the octave that puts
that pitch class nearest, which is a rounding rather than a search — and the notes
left out contribute nothing at all, so a five-note scale costs a little over half
what a nine-note one does. Both ends of the range collapse: all twelve is the
nearest semitone, in two ops, and none at all is a wire.

**In key** is the preset for it, and the one to read the patch of if the module
seems abstract. A noise field wanders over two octaves and a Quantiser pulls it
onto a pentatonic, which is the scale with no wrong note in it — so a signal with
no idea what key it is in comes out as a melody. Half a minute of it plays A2 C3
D3 E3 G3 A3 C4 D4 E4 G4 A4 and nothing else.

The gate that opens the envelope also goes into the Quantiser's `hold`, and that
is not a detail. Snapping a note cleanly is only half of playing in a key; the
other half is that the pitch has to be settled before a note starts and stay
settled until it has finished. Left free, the field crossed into the next note of
the scale at whatever moment it happened to, which was as often as not in the
middle of one. The pitch stepped perfectly when it did — and a perfect step in
the middle of a held note is not heard as a new note at all: with the phase
carried across (ADR-0030) there is no onset to mark it, so the ear takes it for
the note it was already listening to, *sliding*. With that second wire in, the
interval the note is frozen for is the interval it is sounding for, by
construction rather than by arithmetic.

`hold` is also where the two sinks part company, and it is the second thing in
the patch read two ways. The ear needs the note to stop moving while one is
sounding; the eye needs it not to, or the picture would snap on the beat instead
of drifting. A hold is exactly that difference — it holds where there is a
previous evaluation to hold from, and there is none on the screen.

The Noise is one module read two ways, which is what makes the picture honest
rather than illustrative. At the speakers there is no pixel, so `x` and `y` are
nothing and what the ear gets is a walk along `z` alone. On screen the same
module reads the pixel's own position, so what the eye gets is that wander laid
out across the frame — terraced into flat bands of colour, one per note, with the
unsnapped field still visible underneath as brightness. The band widths are
uneven because a pentatonic's gaps are two and three semitones rather than one:
the widths *are* the scale. The note you are hearing is the colour in the middle
of the picture.

### Playing a recording

The **Sample** module reads a WAV. `in` is how far into it to read, *in seconds*
— and because that is a domain it is normalled to Time, so a player dropped on
the canvas plays the file once at its recorded speed and then stops.

`trigger` is the socket that makes it an instrument. On a rising edge it takes
the position it arrived at as the start of the clip, so the file plays from its
beginning at its own pitch and tempo — and an edge arriving *while it is still
sounding* cuts that short and starts again, which is what a drum machine does
with a fast roll. Patch the same gate that opens an envelope. It is an edge and
not a level, so the width does not matter: a spike one evaluation wide fires it
exactly as a long gate does. Left alone it does nothing, and the player is what
it always was.

The trigger runs no playhead of its own. It remembers where `in` had got to when
the last edge came, and reads the difference — so the retrigger falls out rather
than being handled, and driving `in` with something other than the clock
re-zeroes against that instead.

There is one wrinkle: a player with a trigger wired in still plays once as the
patch begins, before any edge has arrived. Until then it is a player with no
trigger, and that is what one of those does — telling the two apart would mean
knowing whether the socket is patched, which nothing here can ask. A gate on the
output from the same trigger is the fix, and a drum wants one anyway.

Everything else is what you drive it with rather than a knob it carries: a `Saw`
times `length` loops it, a negative slope plays it backwards, `Time × 2` is
double speed an octave up, and an envelope into `in` scrubs. Off either end is
silence, which is how a one-shot ends. The second output is the clip's length in
seconds, so a patch can loop or rescale without being told how long the file is.
Reading by position rather than by rate also means the file's own sample rate
never comes into it — a 44.1 kHz clip plays at the right pitch in a 48 kHz render
because "half a second in" means the same thing to both.

**A patch names its sample rather than carrying it**
([ADR-0052](docs/adr/0052-a-patch-names-its-samples-rather-than-carrying-them.md)).
That is the one place a `.fbk` stops being self-contained, and it is not a
shortcut: the undo history snapshots the whole document on every edit and keeps
two hundred of them, so a megabyte of PCM in the file would be a megabyte per
undo step and a re-serialisation of it per knob turn.

So a file that has moved is reported by name, through the same channel as every
other complaint — the status bar, the assistant's issue list, and
`flyback-cli check`'s exit code:

```
broken.fbk
  error    Sample: 'Sample' cannot read missing.wav — there is no file there.
1 error, 0 warnings.
```

A relative path is measured from wherever the patch is, so a patch and the sounds
beside it travel together. The patch still compiles — to silence where the
recording would have been — so the editor goes on drawing while you find it.

Mono, and WAV only: 8 to 32 bit PCM or 32/64 bit float, with a stereo file summed
on the way in because the op that reads one is scalar like every other signal
here.

The eye reads clips as well as the ear, which is what lets you point a **Probe**
at a player and see the waveform — the Probe sweeps time across the picture, so
each column is the clip at a different moment. A shader has no recording to read,
though, so a patch whose *picture* reaches a Sample is drawn on the processor for
as long as it does, and the status bar says so. Nothing else pays for that: dead
code is eliminated per sink, so a player wired only to `left` leaves the picture
on the shader exactly as before.

What a Probe **cannot** show is the triggering. A trigger is something that
happened before now, and the screen has no before — the same reason a Probe
cannot show a delay line or an accumulated phase. So the chart is the clip read
at `in` with the trigger ignored, which for an `in` on the clock means you see
the file only while the transport is inside it: rewind, or set `window` to about
the clip's length, and the waveform is there. A **Scope** shows the triggering,
because it is a recording of what came out rather than a second evaluation.

### Showing a picture

The **Image** module reads a PNG, and it is the one module in the catalogue that
is not arithmetic. Everything else here works out what it draws; this one brings
a photograph in from outside.

It is placed at its own shape — filling the height, reaching its own aspect
either side of the middle, black beyond all four edges. Which gives the property
the whole thing is worth having for: **a frame this program rendered, read back
in, is exactly the frame it was.**

```bash
flyback-cli render nebula.fbk -o frame.png --size 480x270 --at 3
```

Point an Image at `frame.png`, render *that* patch to a PNG, and the two files
are byte for byte the same. From there the picture is a field like any other —
`x` and `y` are where it is being asked about, so a **Scale** zooms it, a
**Translate** slides it, a **Rotate** turns it, a **Kaleidoscope** folds it and a
**Warp** bends it. The module decides nothing about the mapping except what it is
when nothing is patched in.

Unlike a Sample, **this keeps the GPU**. That is the whole reason it is an opcode
rather than a fudge: a clip is a buffer a shader has nowhere to put, and a picture
is a texture, which is what a shader is made to read — so a photograph goes up
as one and the preview never leaves the shader
([ADR-0059](docs/adr/0059-a-picture-comes-in-as-a-texture.md)). One texture per
distinct file, so a patch showing the same picture in four places uploads it once.

Everything else follows the sample's rules, because they are the same rules: the
patch stores the *path*, a relative one is measured from wherever the patch is, a
file that has moved is reported by name, and the patch still compiles — to black
where the picture would have been. Transparency is taken as black too, since a
colour here is three numbers and not four, and that is the same answer everywhere
outside the picture's own edges gets.

The decoder is ours, for the reason the writer already was: no imaging dependency
in the engine ([ADR-0019](docs/adr/0019-no-third-party-dependencies-in-the-engine.md)),
which makes the two a pair — what this program can read is what it can write. Every
colour type at 8 and 16 bits. Interlaced files and sub-byte depths are refused by
name rather than read wrongly, and a moving picture is not here at all: a video is
a container and a codec each, where a PNG's compression came out of the framework.

Silent, and not merely quiet: the speakers' walk is handed no picture library, so
on that path an Image is black and the file is never even opened.

### Playing it

Everything above is a patch reading itself. **MIDI In** is the module that lets a
hand in: it has no inputs at all and four outputs — `pitch` as a note number,
`gate` while a key is down, `velocity`, and `trigger`, a single evaluation high
each time a note is struck. Into a `Note` and an `ADSR` that is an instrument you
play; into a `Sample`'s own `trigger` it is a drum pad.

Its panel asks which keyboard, and the list is whatever is plugged in with the
**computer keyboard** in front of it. Plug one in with the panel already open and
it is there the next time the list is opened, rather than after clicking away and
back. A device that a patch names and this machine has not got is left named
rather than quietly swapped — the patch means that keyboard, and the status bar
says it is not here.

A fresh one listens to the computer keyboard, which needs no driver and is always
there — the bottom two rows are the white and black notes of an octave in the
tracker layout, the top two rows are the octave above, and Page Up and Page Down
move both. So `Z` is C3, `S` is the C
sharp over it, `Q` is the C above them all, and the status bar says where the
rows have got to when you move them. The keys are read by position rather than by
the letter printed on them, so it stays a piano on a keyboard whose letters are
somewhere else.

The letters only play while something in the patch is actually listening to them.
That is answered off the compiled programs rather than off the canvas — a MIDI In
sitting there wired to nothing is not read by either sink, and takes no
keystrokes. Typing into a box is never playing.

**A note is a bare keystroke and nothing else.** Anything carrying `Ctrl`, `Cmd`
or `Alt` is left for whatever claimed it, so `Ctrl+Z` is undo in a patch being
played with a hand on the Z, and so are `Ctrl+Y`, `Ctrl+L` and the clipboard —
every one of which lands on a letter the layout also plays. `Shift` is not one of
them: no gesture here is Shift and a letter, so a capital Z is still a Z. Framing
moved from `F` to `Ctrl+F` to keep the rule whole; `F` was not in the layout that
ships, but a gesture that worked only until somebody added a key to it is worse
than one that reads like its neighbours.

Letting a key go carries none of those guards, and that asymmetry is deliberate:
a key going down can start something and has to be sure it was meant, and a key
coming up can only ever stop one. Take hold of `Ctrl` mid-note, delete the
module, click into a text box — the note still ends. Every guard in front of a
release is a way for one to be missed, and a missed release lasts the rest of the
session.

**One note at a time**, with the newest key taking the voice: letting it go hands
the voice back to whichever key is still down, so a trill is a trill rather than
a stutter. The gate still reopens for a fraction of a sample at each new note,
which is what makes an envelope articulate every note of a run played legato
instead of sliding through it.

The picture is played as well as the speakers, which is the point of putting this
in a video synth. Both backends read it: the interpreter is handed the block of
live values beside the delay lines, and the shader is handed them as uniforms
uploaded before each frame — one reading of the keys per frame, and one per audio
buffer, so nothing is ever half of two moments. What is **not** played is an
export: a rendered movie has nobody at the keys, so every output of this module
sits at rest and the patch draws what it draws with no note down. A patch holding
one is a performance rather than a recipe, and that is the one place where the
file is no longer the whole of what you will get.

A device that is not there is said rather than swapped. Pick a keyboard, save the
patch, open it on a machine without one, and the module still names it and the
status bar explains the silence — the same bargain a Sample makes with a file
that has moved.

**Played** is the preset for it, and the one to open if the module seems
abstract. `pitch` goes into a Note for the ear and — through a Clamp into four
octaves — picks both the hue and how tight the rings are for the eye. `gate`
opens an ADSR, which is the note at the speakers and the light on the screen.
`trigger` does the thing only it can: it fires a **Sample & Hold** that catches a
wandering Noise and parks it on the Pulse's `width`, so every note struck has a
timbre of its own, settled the instant it starts and steady for as long as you
hold it. Without the hold, the same Noise straight into `width` would smear the
tone about *while* a note was sounding, which is a different and much less
musical instrument.

Velocity is deliberately left unwired: a typist strikes every key the same, so a
wire from it would be one that does nothing until hardware arrives — better an
obvious empty socket than a knob that lies. The trigger reaches the ear and not
the eye, for the reason above, and the Sample & Hold is in the same position and
stops holding there for the same one. So the picture is given the two outputs
that mean something with no past behind them: which note, and whether one is
down. Nothing held reads as note nought — the same answer a program nobody is
playing gives — which the Clamp turns into the bottom of the range rather than
into wherever nought happened to land.

Hardware sits behind the same seam the sound devices do
([ADR-0025](docs/adr/0025-platform-io-behind-loadable-plugins.md)): `IMidiInput`
offers a backend and `IMidiPort` is one device that is open, split the way
`IAudioOutput` and `IAudioDevice` are. Whatever is plugged in joins the computer's
own keys in the same picker, and the module cannot tell them apart — an instrument
is named by a string, and nothing above that line knows what is behind one.
All three systems are covered: Windows through the multimedia library it already
has, Linux through the ALSA sequencer, and macOS through CoreMIDI. The last two
are routing tables rather than lists of hardware — a keyboard, another program's
port, PipeWire's bridge, the IAC bus macOS ships — so what a picker offers there
is everything on the machine that plays notes rather than only the things with
sockets on them.

A device is only held while something is listening to it. A MIDI In wired to
nothing has been compiled away, so it opens nothing and the keyboard stays free
for whatever else wants it; wiring one up opens the device, and unwiring it hands
the device back. Every channel plays the one voice, since the module has nowhere
to name a channel.
[ADR-0056](docs/adr/0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md)
records what a live value costs the engine — one opcode, and the first thing a
program can read that may be different on the very next evaluation.

Audio runs at 48 kHz, 4× oversampled and filtered before decimation, which keeps
the naive `Saw` and `Square` from folding harmonics back down as buzzing. It
reduces aliasing rather than removing it. While sound plays it is the master
clock and the picture follows, since audio cannot be stretched to catch up.

The device itself comes from a plugin, so the application has no platform-specific
code in it at all. WASAPI covers Windows, CoreAudio covers macOS and ALSA covers
Linux — routed by PipeWire or PulseAudio where either is running — and each ships
only in the build for its own platform. Where none of them can open a device the
**Audio on** button is disabled and says why; everything else, including WAV
export, works unchanged.

## Plugins

Anything that differs per machine is loaded from disk at startup rather than
referenced at build time. One folder per plugin, beside the executable:

```
Flyback.exe
plugins/
  Wasapi/
    Flyback.Plugins.Wasapi.dll
    Flyback.Plugins.Wasapi.deps.json
    NAudio.dll …
  Supersaw/
    Flyback.Plugins.Supersaw.dll
    Flyback.Plugins.Supersaw.deps.json
```

The status bar says which backend is playing; hover it for what loaded, what did
not and why, and where the folder is.

A sound backend is one class that offers itself and one that opens the device.
Being asked whether you are supported must not open anything, so a plugin for
another operating system stays loadable everywhere:

```csharp
public sealed class JackPlugin : IFlybackPlugin
{
    public PluginInfo Info { get; } = new("jack", "JACK output", "Low-latency Linux sound via JACK.");

    public void Register(IPluginRegistry registry) => registry.AddAudioOutput(new JackOutput());
}

public sealed class JackOutput : IAudioOutput
{
    public string Id => "jack";
    public string Name => "JACK";
    public int Priority => 150;                         // the shipped ALSA backend is 100, and loses to this
    public bool IsSupported => OperatingSystem.IsLinux() && JackIsRunning();

    public IAudioDevice Create(AudioFormat format) => new JackDevice(format);
}
```

`IAudioDevice.Start` is handed a callback that fills an interleaved buffer on the
audio thread; it must not block, allocate or throw. That is the entire contract —
`Flyback.Plugins` has no dependencies, and neither need you.

Reference `Flyback.Plugins` **and** `Flyback.Core` with `Private="false"
ExcludeAssets="runtime"` — both are host-owned, and naming only the first leaves
the second to be copied in as a transitive reference. Set `EnableDynamicLoading`
so the `.deps.json` the loader reads is produced, and drop the build output into
a folder under `plugins/`. To ship one in the box instead, add a line to
`Flyback.App.csproj`:

```xml
<PluginProject Include="..\Flyback.Plugins.Alsa\Flyback.Plugins.Alsa.csproj" FolderName="Alsa" Platform="linux" />
```

`Platform` is optional and names the one platform the plugin is for, matching the
leading part of a runtime identifier. A plugin that declares one is left out of
output built for anywhere else; a plugin without one goes everywhere, which is
what a module plugin wants.

Each plugin loads into its own `AssemblyLoadContext`, so two of them may depend
on different versions of the same package. A broken, missing or hostile plugin is
a note in the status bar, never a program that fails to start.

### Modules from a plugin

A plugin can also add modules. They are the same data as the built-in ones — the
`NodeDef` from *Adding a module* below, written in another assembly:

```csharp
public sealed class SupersawPlugin : IFlybackPlugin
{
    private static readonly ModuleProvider Provider = new("flyback.supersaw", "Supersaw");

    public PluginInfo Info { get; } = new("flyback.supersaw", "Supersaw");

    public void Register(IPluginRegistry registry) => registry.AddModules(Provider,
    [
        new NodeDef("flyback.supersaw.osc", "Supersaw", "Oscillator", …),
    ]);
}
```

Every type id must start with the provider's id and a dot. That rule is what
makes shadowing a built-in impossible, and what lets a saved patch name the
plugin a module came from without having the plugin to ask.

A plugin can offer **presets** too, which is how it ships a patch showing its
modules wired properly. They appear in the Patch dropdown after the engine's own
— the app always opens on `Plasma`, so what you see at startup never depends on
what is installed:

```csharp
registry.AddPresets([new PatchPreset("Supersaw", SupersawPreset.Build)]);
```

A preset is handed the catalogue when it is picked rather than when it is
registered, so it can freely use the modules the same plugin just added.

Because a patch is keyed by type id, one that uses a plugin's modules writes
down what it needs:

```json
{
  "Requires": [ { "Id": "flyback.supersaw", "Name": "Supersaw" } ],
  "Nodes": [ … ]
}
```

Open it somewhere that plugin is not installed and it does not open — you get
*"This patch needs Supersaw (flyback.supersaw)"*, and whatever you already
had stays where it was. A patch that uses only the modules in the engine records
nothing and is written exactly as it always was.

Plugins are read once at startup, so installing one means restarting.

### Supersaw

[`src/Flyback.Plugins.Supersaw`](src/Flyback.Plugins.Supersaw) ships in the box
and is the worked example — seven detuned saws in one module, under a hundred
lines.

Pick **Supersaw** from the Patch dropdown for it wired up: 110 Hz from a
Frequency module, both outputs to their own channel, and one slow sweep driving
the detune of the sound and of the picture at once, so the bands on screen beat
in step with what you hear. `freq` counts cycles per unit of `in` like every
other oscillator here — reach for its knob instead of a Frequency module and you
get a one-hertz saw, which is a click rather than a note.

Seven voices, not a knob's worth: an emit function runs once at compile time and
writes straight-line ops, so it never sees a knob value and a variable voice
count would mean a variable number of instructions. Seven is what the sound is.
The whole thing unrolls to about seventy ops, no branches and no state, and it
is peak-normalised as it mixes — turning `mix` up makes it wider, never louder.

Point it at the screen instead of the speakers and the detuning reads as a slow
moiré, which is the same beating seen rather than heard.

### Delay and reverb

[`src/Flyback.Plugins.Space`](src/Flyback.Plugins.Space) adds **Delay** and
**Reverb**, and the **Echo chamber** preset puts a plucked tone through both.
They are the two that remember longest: everything else here with a memory —
an oscillator's phase, the Filter and Phaser below — remembers one evaluation,
and a reverb tail is thousands.

The Reverb is eight damped feedback combs into two chains of four allpasses,
which is the Schroeder arrangement with Moorer's correction to it. The damping
is the part that matters: a comb with a plain gain in its loop hands every
repeat back as bright as the last, so the tail keeps one timbre all the way
down, and that fixed metallic ring is what a cheap reverb sounds like. A room
loses its highs first, and this one does too — at a fixed corner rather than on
a knob, because how absorbent a wall is describes the room and is not a gesture
anyone performs. The two allpass chains differ only in their lengths, so `out`
and `wide` are one tail smeared two ways — patch both for stereo, or take `out`
alone for the mono version.

Everything else here is a pure function of `(x, y, t)`, which is what lets the
video renderer run rows in parallel. These two are not, so **they only work on
the audio path**: the video program is given no state, and both become wires.
That asymmetry is the price of
having them at all, and [ADR-0027](docs/adr/0027-delay-lines-give-the-audio-path-a-memory.md)
sets out why it cannot be avoided. For a picture with a past, use `Feedback`.

An oscillator's phase is carried the same way and falls back the same way, but
you will not notice: what it falls back *to* is the multiply it replaced, so the
picture is identical either way.

### Filter and fold

[`src/Flyback.Plugins.Timbre`](src/Flyback.Plugins.Timbre) adds **Filter**,
**Fold** and **Drive** — the modules that decide what a patch sounds like rather
than where its signal goes. Until they existed the catalogue had five
oscillators and nothing whatever to shape one with. The **Filter sweep** preset
is all three of them wired up.

The Filter is a state-variable one, with `low`, `band` and `high` coming out at
once rather than one at a time behind a switch.

The two shaping modules are pure and untyped, so they do the same thing at both
sinks and to a colour as readily as to a tone: the harmonics the ear hears from
a Fold are the bands the eye sees in a gradient, from one knob. That is what the
preset shows — one ring field and one saw through the same folder at the same
drive, so the picture bands exactly as the tone brightens.

The Filter is the one with a memory, and it is audio-only for the reason
everything else here with a memory is. What it does on the video path is a
choice rather than an accident, though: a picture is one evaluation per pixel
with nothing before it, so what the filter sees there is a signal that never
moves — and its response to a signal that never moves is everything through the
lowpass and nothing through the other two. Put one in a patch and the picture is
the picture it already was.

Two things are worth knowing about the sound. The cutoff is in hertz and means
it: the coefficient is prewarped, so the corner lands on the frequency asked for
rather than near it. And the topology solves its own feedback loop rather than
iterating it, which is what keeps a fast sweep stable at every cutoff it passes
through on the way to wherever it is going.

None of this needed the engine changed, which is the part worth reading the
plugin for. The filter's integrators are one-evaluation cells taken from the
emitter — the same cells the Unit Delay uses to let a patch draw a loop by
hand — and it works out its own sample rate by measuring how far the clock moved,
the way an oscillator measures its own domain. So a filter, a slew limiter or a
sample and hold is now something anybody can write outside `Flyback.Core`; what
still cannot be is anything needing a *buffer*, which is why Delay and Allpass
remain opcodes. [ADR-0041](docs/adr/0041-a-plugin-can-hold-state-without-a-new-opcode.md)
sets out the whole of it.

### Chorus, flanger and phaser

[`src/Flyback.Plugins.Modulation`](src/Flyback.Plugins.Modulation) adds the three
effects that move on their own. Each is a copy of the signal put slightly out of
step with itself and mixed back in, and the whole difference between them is how
far out of step and by what means. The **Moving parts** preset is all three in
the order a pedalboard would have them.

The chorus delays by about 15 ms; the flanger by an order of magnitude less.

All three carry their own sweep rather than taking one on a socket, which is the
one place this plugin departs from how the rest of the synth is wired. The
argument for a socket is real and the argument against is stronger: the effect
*is* its own movement, and a chorus whose sweep has to be assembled by hand out
of a Sine and a Remap is three modules pretending to be one.

What it costs is paid back on an extra output. Each module hands its sweep out on
`lfo`, so the movement can drive something else — and that output is the one part
of these modules the picture can use, since a phase accumulator falls back to the
multiply it replaced where there is no state. The preset is built on exactly
that: the chorus slides the image, the phaser picks the hue and the flanger
drains the colour out of it, so the three motions the eye sees are the three the
ear hears. Nothing is duplicated between the sinks.

Reaching for an LFO does cost the module that produces it. Dead code is
eliminated a module at a time rather than an op at a time — an emit function runs
whole or not at all — so a picture wanting the sweep also gets the delay lines
beside it. They are inert there rather than wasteful: with no state a line is a
wire.

The Chorus and the Flanger are delay lines and are audio-only for the reason
every delay line here is. The Phaser is not a delay line at all: it is four
one-evaluation cells, which is what a first-order allpass needs and what
[ADR-0041](docs/adr/0041-a-plugin-can-hold-state-without-a-new-opcode.md) made
possible without an opcode. It is also what made the machinery worth sharing —
the interval a module measures its own sample rate with, and the flag it asks
whether there is any memory at all, now belong to the emitter rather than to
each module that wants them
([ADR-0042](docs/adr/0042-the-clock-and-the-memory-flag-belong-to-the-emitter.md)).
All three are exactly a wire at a mix of zero, and on the picture.

### Whole rack

The showcase, and the one preset that reaches across a plugin boundary: all six
modules the two plugins added, in one chain and in the order a rack would have
them. A note sequencer plays a riff; a saw is folded and driven into harmonics,
then filtered back out of them; the phaser, flanger and chorus move what is left,
and the chorus's two outputs make it stereo.

The gesture worth listening for is the **gate opening the filter as well as the
note**. One Remap from the sequencer's `gate` to the Filter's `cutoff` is the
whole envelope — four ops — and it is the difference between a note that starts
and a note that arrives.

On screen, the Fold does the work, because it is the only one of the six that
means anything to a picture. Rings travel outward with as many of them as the
step the tune has reached, the same slow sine that folds the tone folds them into
bands, and the gate that opens the filter brightens the image on the beat.

Its colour drifts from a sine of its own rather than from a chorus's `lfo`, which
is how *Moving parts* does it and is the one thing here done deliberately
differently. A module is resolved whole: reaching for one of its outputs compiles
everything upstream of *all* its inputs, because an emit function is one function
and the compiler cannot know that `lfo` ignores `in`. At the end of a chain six
effects long that costs the video program the entire chain — 314 ops against the
141 of the next most expensive preset that ships. A separate sine costs eleven.

A preset that needs a plugin cannot record that the way a saved patch does, since
it is code rather than a file. It is built when it is picked rather than when it
is registered, so a missing plugin surfaces there, and this one checks first so
that what you get is *"it needs the Filter and fold plugin"* rather than the id
of a module nobody asked about.

### Shapes

[`src/Flyback.Plugins.Shapes`](src/Flyback.Plugins.Shapes) adds **Circle**,
**Box**, **Polygon**, **Star**, **Combine** and **Fill** — and is the first
plugin written for the eye rather than the ear. Every module the picture had was
an infinite field: Noise, Checker, Rings and the oscillators go on for ever in
every direction, and the eight under Space bend the plane they go on for ever
across. So a patch could make a texture of any kind and could not make a *thing*.
There was no circle.

**A shape here is a distance rather than a picture of itself** — one number per
point, negative inside the form, zero on its edge, positive outside, in the same
units the Coordinates module hands out. That is the whole plugin, and everything
else falls out of it. Union is the smaller of two distances and intersection the
larger, which is to say Minimum and Maximum, which the catalogue has always had.
Growing a shape is subtracting from it. An outline is the distance to the edge
with the sign thrown away.

So a shape's output is not something to look at until a **Fill** has turned it
into ink: 1 inside, 0 outside, and an edge as soft as you ask for. There is one
Fill for a whole assembly of forms rather than a fill knob on each of them, and
it hands out the form and its own outline together — two readings of one number,
the way the Filter hands out three responses at once. **Combine** is the other
module that could not be assembled from what was there: the same three operations
Minimum and Maximum already do, with a seam that melts the crease where two forms
meet, so two blobs flow into one another instead of overlapping.

Sizes are in the picture's own units and there is no pixel anywhere, because
nothing in a compiled program knows how big the frame is — the same patch is
drawn at preview size, at export size and into a movie. A softness of a hundredth
is three pixels on a 540-line preview and six on a 1080-line render, which is
what makes a still and the preview of it the same image.

Two presets ship with it. **Four forms** is the showcase: all four shapes on a
ring, rocking, with the seam between them opening and closing — so they are four
separate things, then one body with a hole through the middle, then four again. A
Minimum would have put them in one picture perfectly well; what it cannot do is
the crease, and four shapes meeting at corners read as four shapes overlapping.
The same sweep that melts the seam is the Pulse's width, so what the eye sees as
four forms flowing together the ear hears as a thin buzz opening out into a
hollow square. One number, arriving in two places.

The **Shape scan** preset is a star with a hole cut through it, rocking, and the
same field read as a waveform. That is the part worth having the sound on for:
the Scan sweeps a loop through the distance field and hands what it passes over
to the speakers, so the star's five points are five bumps in every cycle and
sharpening them brightens the tone. Nothing here has a memory, so both sinks run
the same arithmetic — what you hear is the shape rather than something chosen to
go with it.

Nothing needed the engine changed, not even the one-evaluation cells the Filter
takes. That matters more here than it would for a module aimed at the speakers:
the preview draws on the GPU wherever it can, and a video program containing a
table read — a clip or a trace — cannot be drawn by the shader at all and takes
the preview back to the CPU, quietly, for as long as the patch is loaded. All six
of these are arithmetic over x and y, so all six survive to the shader, and there
is a test that says so in every dialect.
[ADR-0057](docs/adr/0057-a-shape-is-a-distance-and-one-module-inks-it.md) sets
out the whole of it, including what the eye is still missing — which is more than
what arrived.

### Noise

[`src/Flyback.Plugins.Noise`](src/Flyback.Plugins.Noise) adds **Fractal** and
**Cells**. What shipped in the engine was one octave of value noise — a field of
smooth blobs, all the same size, which is a texture nothing in the world has.
Every picture that looks like weather, stone, smoke, terrain, rust or skin is
built out of one of these two, and a patch could assemble neither: the first
would have taken eight Noise modules and thirty wires, and the second is not
assemblable at all, because it needs one field read at nine places that depend on
where you are.

**Fractal** is the same noise summed at several sizes — each octave twice the
frequency and a fraction of the height of the one before. `smooth` adds them as
they come and is cloud; `folded` adds each one's distance from the middle
instead, which creases the field everywhere the noise crossed it and is smoke,
flame and hammered metal. One minus `folded` is ridges, and a mountain — one
Subtract, which is why it is not a third output. `roughness` is how much each
octave keeps of the last: at 0 the module is exactly a single **Noise**, and the
tests say so to nine decimal places.

**The octave count is on the node rather than on a socket**, and it is the one
thing here worth reading twice. Every other number is a value the program
computes with; this one decides how long the program *is*. A socket could not say
it — the program would have to cover eight octaves however few were asked for,
and every patch would pay for the most anybody might want. So it is carried, the
way a Quantiser carries its scale
([ADR-0051](docs/adr/0051-a-quantisers-scale-is-a-set-on-the-node.md)), and
declared rather than drawn
([ADR-0055](docs/adr/0055-a-plugins-extra-declares-its-editor.md)) so that no
plugin ships a control. It is the first shipped plugin to carry anything at all.

**Cells** is the other half of the subject and nothing like the first: value
noise is smooth everywhere and can only look like weather, and this is built out
of distance to scattered points, so it has *edges*. `distance` shades each cell
outward from its point; `edge` goes to nothing exactly on the line between two
cells, so a Threshold on it is a crack; and `cell` is one flat number for a whole
cell and a different one next door — the only value in the catalogue that is
constant across a region and jumps at its border, which is what makes a mosaic.

It is also the most expensive module here by a wide margin, and the reason is
worth knowing. What it wants per square is a *hash*, and the machine has no hash
op. The one a shader would normally use — `fract(sin(x) * 43758.5)` — is a way of
turning rounding error into randomness, and gives a different answer at every
precision: the interpreter and the shader would draw different cells, which is
much worse than drawing them slowly. Noise is the only agreed randomness in the
machine, so Cells is nine squares times two lookups, eighteen noise a pixel. On
the GPU that is nothing much; on the interpreter — the preview with the shader
off, and every command-line render — expect a still to take seconds.

The **Marble** preset is the third famous thing in the subject, and needs no
module for it: a Fractal into the **Warp** the catalogue has always had, into a
second Fractal. Three wires, and the veins stop running where the noise happens
to and start running where they were pushed. Nothing is sent to both sinks — the
speakers have no pixel, so what the ear gets of each field is the slow wander it
makes at the origin, one drifting the pitch and the other swelling the level.

### Colour

[`src/Flyback.Plugins.Colour`](src/Flyback.Plugins.Colour) adds **Palette**, **To
HSV**, **Grade** and **Posterise**. The catalogue had five colour modules and
between them they could build a colour, take one apart into channels, blend two
and multiply one. What they could not do was choose a colour well, read one back,
or change one after the fact — so every picture the machine made was coloured the
same way, by putting a signal into HSV's hue, and every one came out a rainbow.

**Palette** is the one that changes what patches look like. A hue sweep walks the
whole wheel and passes through every colour there is on the way to the one that
was wanted; a palette walks a handful that are neighbours. It is Iñigo Quílez's
cosine palette — `brightness + contrast · cos(2π(cycles · t + offset))`,
evaluated three times with the channels' offsets a fixed step apart — and that
step, `spread`, is the knob to reach for. A third is exactly the rainbow, because
three channels a third of a cycle apart is what a hue sweep is. Below it the
channels move nearly together and the palette runs through tints of one colour:
the sunsets, the teals, the golds. At nothing it is grey. **Every value of it is
a palette somebody could have meant**, which is the useful property — a knob that
cannot be turned to something ugly.

**To HSV** is the missing inverse, and the one real asymmetry the catalogue had:
Split is RGB's own inverse and nothing was HSV's, so a patch could build a colour
and never read one. That cost anything depending on the colour a patch already
has — rotating a hue, keying on one, taking the saturation out of a picture
without touching what colour it was. It is written without a branch, because the
register machine has none: which channel is largest picks one of three
expressions by being multiplied by whether it won. The test that matters is the
round trip, and it holds to four decimal places over the whole wheel.

**Grade** is the three adjustments a finished picture wants, none of which is the
multiply that Gain already was. Saturation mixes towards the picture's own
brightness — the weighted one the eye uses, so a pure green greys to something
bright and a pure blue to something dark. Contrast leans on the middle grey
rather than on black, which is the whole difference between contrast and gain.
Gamma deepens what is under the middle and leaves white alone. All three are
neutral at 1, and in that state the module is exactly a wire.

**Posterise** is three ops nobody finds. The levels are placed on the ends rather
than between them, which is the part that is easy to get wrong — the obvious
arithmetic never reaches white — so black stays black, white stays white, and
posterising a posterised picture changes nothing. Each channel is stepped on its
own, so the three sets of bands cross and there are many more than `levels`
colours in the result.

The **Spectrum** preset is deliberately the Plasma preset's own two sines, so
that what is being shown is the colour and nothing else: one slow sweep walks
`spread` from nothing to a third, and the picture travels from tints of one
colour, through the sunsets in between, to the rainbow Plasma is stuck at. The
same wire opens a pulse from one partial to a stack, so the sound does what the
colour does.

## Using the editor

The inspector lists every gesture whenever nothing is selected — adding a
module, patching, unplugging, selecting, panning, copy and paste, delete,
framing, renaming — and every toolbar symbol says what it is if you rest on it.
Three things are on neither: `Ctrl+Y` redoes as well as `Ctrl+Shift+Z`, `Cmd`
does whatever `Ctrl` does, and the Output is the one module `Delete` will not
take.

Every gesture on a letter carries `Ctrl`, and that is a rule rather than a
convention: bare letters belong to the instrument, since a patch holding a
[MIDI In](#playing-it) plays them. `Delete`, `Space` and `Escape` are the
exceptions, being keys no keyboard layout has a note on.

The lists — the patch to start from, the preview size, the instrument a MIDI In
listens to — are pointed at rather than typed at. A dropdown ordinarily answers
the keyboard twice over, an arrow moving the selection and a letter jumping to
the first entry beginning with it, and both *commit*: arrowing down the patch
list would throw the patch away once per step. So a keystroke at a focused list
does nothing to it, and goes on to mean whatever it would have meant with nothing
focused.

`Ctrl+G` draws a selection as one box and `Ctrl+Shift+G` puts it back;
double-clicking a box opens it, and double-clicking the strip above an open one
shuts it again. Both are on the inspector too, above the delete button.

A box is a fact about the canvas and about nothing else. The modules stay where
they were, the wires between them stay as they were drawn, and the compiler is
never told — a patch sounds and looks exactly the same whether it is boxed up or
laid out flat, and a build that knew nothing about groups would read the file and
simply draw everything separately.

Its sockets are not declared, they are *put there by wiring*: drawing a wire
across the edge adds one, named for the module and port inside that it stands for
— `filter.cutoff`. Wires with both ends inside are hidden, an input nothing has
ever been wired to is not a socket at all, and several wires leaving one inner
output arrive at one socket the way fan-out already looks anywhere else. So
renaming a module inside relabels the edge for nothing.

**Taking a wire off leaves the socket.** The edge of a box is a thing you arrange
rather than a thing that happens to you, and a socket that vanished when you
unplugged it would shrink the box under the hand that had just unplugged
something — and could never be plugged back into. So the wire goes and the socket
stays; the inspector lists them and offers an `✕` on any with nothing in it, and
a wire puts it straight back. Which is also what makes patching *into* a shut box
possible: the socket is there to aim at.

What is written down is the module and the port, exactly as a wire already
records them — never a numbered port of the box's own. That is the distinction
that keeps this safe: nothing outside a group ever refers to a socket by
position, so rearranging the inside renumbers nothing.

One thing does follow from being only a drawing: grouping cannot make a
*definition*. Two boxes made the same way are two boxes, and editing one does
nothing to the other.

It takes two modules. A box round one shows exactly the sockets that module
already has, in the same order, so it is the module again with a second name and
one more thing to open — `Ctrl+G` declines and says why. The rule holds after an
edit as well as at the moment one is made: delete your way down to a single
member and the box goes, leaving an ordinary module behind. The Output is never
in one, so selecting everything and grouping it groups everything else.

Copy and paste carry the box along with what is in it — the modules, the wires
between them, the name on the header, and the sockets on the edge including the
ones nothing is wired to. What arrives is a second box and not the same one:
fresh ids throughout, so editing it does nothing to the one it came from, which
is the same thing being a drawing rather than a definition says everywhere else.
Only whole boxes come. Copying half of one pastes those modules loose, for the
same reason a wire with one end outside the selection is left behind — a box
short of a member would arrive a different shape, with different sockets, from
the one on the canvas.

**Save to palette** keeps a box for good. The button is in the group's own panel,
and what the list will call it is its name — so a group that has none is offered
the button greyed, and the title above it renames on a double-click. Kept groups
are listed under `GROUPS` at the top of the module list, above the catalogue, and
picking one adds it where you right-clicked: the modules, the wires between them,
the knobs as they were left, and the box round the lot — shut, whether or not it
was shut when it was kept, because arriving as one thing is the whole of what a
kept group is for. A double-click opens it. It is a copy and not an instance —
editing what you added does nothing to what is kept, which is the same thing a
group being a drawing rather than a definition says everywhere else.

Each one is a patch file in `groups` beside the settings, which is the whole of
the storage: no library format, no index to fall out of step. So a `.fbk` dropped
into that folder is on the list next time it is opened, and a group kept here can
be mailed to somebody. A `✕` on the row takes one off again — it asks first, in
the row itself, because the entry is a file and there is no undo out there to put
one back with. Saving under a name already kept replaces it, and asks first for
the same reason — the row it would replace is a file, and a name typed twice by
accident is the ordinary way to lose one. Both questions are the same shape: the
thing being asked about turns into `Replace “Voice”?  ✔ ✕` where it stands, and
the cross puts it back.

A wire is re-patched by picking it up at either end, and which end decides what
the gesture is asking. Drag a **connected input** and the plug comes out of that
input; the wire keeps its source and goes looking for a new target — *where
should this go instead*. `Ctrl+drag` an **output** and the plug comes out of
there; the wire stays in the input it feeds and goes looking for a new source —
*where should this come from instead*.

The modifier is on the second one only because an input holds one wire and an
output holds any number: dragging from an output already means *start another*,
which is the common thing to want and could not be given up. So `Ctrl` reaches
for the wire that is there, and only when exactly one leaves that socket. With
none, or with several, it does nothing and you get a new wire — a gesture that
picked one of four for you would be worse than one that declines. Either way,
pulling a wire out and plugging it in again is one press of undo.

Any input with nothing plugged into it uses the value shown on the node, so most
patches need no constant modules at all. The exception is a socket that is
normalled: it names the module driving it in place of a number, has no knob in
the inspector, and the panel says which module and why there is no wire to see.

Undo goes back two hundred edits: a module added, removed or moved, a wire
plugged or pulled, a knob turned, a note edited, a module renamed. A step is the
whole patch rather than an inverse of the edit that made it, so anything that
survives being saved survives being undone and nothing has to be taught what a
particular edit was — which is also why moving a module undoes, having cost
nothing to include.

What counts as one step is the one thing it does have an opinion about. A slider
dragged across its range makes an edit a frame and is one press of undo, not a
hundred; unplugging a wire and plugging it in somewhere else is two edits and one
gesture, and goes back in one press as well. Opening a patch or picking a preset
starts again with nothing behind it: undoing into the patch you had before you
opened a file would not be undo, it would be losing the file. What
the assistant makes is the other way about — it is an edit to your patch rather
than a document in place of it, however much of it changes, so it lands in the
history like anything else and one press of undo has your patch back.

A patch that has been edited and not written out says so with a dot beside the
name in the title bar, and anything that would close it asks first — quitting,
opening a file, picking a preset. The assistant is not among them and does not
need to be: what it does is an edit, so there is nothing there to lose.

The name is whichever way the patch arrived: the file it was opened from or last
written to, or the preset it was built from — `Drone — Flyback`, and with the dot
after it once there is unsaved work. Picking from the preset list renames it the
same way an open does, because that is a document arriving too. The save dialog
offers the same name back, so saving something opened as `drone.fbk` suggests
`drone.fbk`, and saving one built from a preset suggests the preset's name rather
than `patch`.

Cancelling the save dialog cancels the whole thing rather than the save alone:
somebody who asked to save and then thought better of where has not agreed to
lose the patch. Undoing back to what you started with settles it again, for the
same reason a step is the whole document — what is compared is the patch, not
whether anybody typed.

The Output's panel is where the instrument is set rather than the patch: how
large the preview is and whether the processor or the GPU draws it, whether
sound is playing, and the four ways of getting a picture or a sound out of the
program. None of it is saved with the patch — a preview size belongs to the
machine you are working at — but it sits with the block it acts on rather than
along a toolbar.

The status bar carries whatever the compiler wants to say about the patch as it
stands, in amber. Most of it is about the patches that compile perfectly and do
nothing, which is the class of mistake nothing else here would catch.

## Layout

| Project | |
|---|---|
| `src/Flyback.Core` | graph model, compiler, renderer, PNG/WAV/JPEG/AVI writers — no UI dependency |
| `src/Flyback.App` | Avalonia editor and live preview, built in C# without XAML — the window is one class across a file per region ([ADR-0039](docs/adr/0039-one-window-class-across-a-file-per-region.md)) |
| `src/Flyback.Plugins` | the plugin contract and the loader — no dependencies either |
| `src/Flyback.Plugins.Wasapi` | Windows sound output, via NAudio |
| `src/Flyback.Plugins.CoreAudio` | macOS sound output, straight to the default output audio unit |
| `src/Flyback.Plugins.Alsa` | Linux sound output, through libasound's default device |
| `src/Flyback.Plugins.WinMidi` | Windows MIDI input, through winmm — what a **MIDI In** listens to when it is not the computer's own keys |
| `src/Flyback.Plugins.CoreMidi` | the same on macOS, through CoreMIDI — whatever the MIDI server has, which is whatever Audio MIDI Setup shows |
| `src/Flyback.Plugins.AlsaMidi` | the same on Linux, through the ALSA sequencer — hardware, other programs' ports and PipeWire's bridge alike |
| `src/Flyback.Plugins.Supersaw` | the Supersaw oscillator, as a module plugin |
| `src/Flyback.Plugins.Space` | delay and reverb — the only modules that remember more than one evaluation |
| `src/Flyback.Plugins.Timbre` | filter, wavefolder and saturator, holding their state in the emitter's own cells |
| `src/Flyback.Plugins.Modulation` | chorus, flanger and phaser — the effects that carry their own movement |

Why it is built this way is recorded in [docs/adr](docs/adr) — 52 decision
records covering the compiler, the renderer, the shell and the boundaries
between them.

## Tests

```bash
dotnet test Flyback.slnx
```

| Project | |
|---|---|
| `tests/Flyback.Core.Specs` | Gherkin scenarios (Reqnroll) for the behaviour the ADRs specify — dead code, cycles, port typing, input defaults, feedback, guarded arithmetic, coordinate conventions |
| `tests/Flyback.Core.Tests` | snapshot tests of the rendered presets, plus property and fuzz tests over the compiler and interpreter |
| `tests/Flyback.Plugins.Tests` | loads the real shipped plugins off disk — discovery, which backend wins on this machine, and that a type crossing the boundary keeps one identity |
| `tests/Flyback.App.Tests` | the shell: what an edit during playback does to the sound the device is already being handed, and the controls themselves — laid out, clicked and dragged through Avalonia's headless platform |

Each `.feature` file names the ADR it pins, so the records and the specs stay
honest about each other.

Snapshots compare decoded pixels rather than file bytes, because PNG compression
and `MathF` results can both shift without any behaviour changing. When output
legitimately changes, inspect the `.received.png` next to its `.verified.png`
baseline and rename it to approve.

The fuzzer generates random well-formed patches and pushes them through compile
and render. It is the only test that reaches all 65 modules, and it is what
guards the gap ADR-0008 describes: nothing links a module's declared ports to
what its emit function actually indexes.

## Adding a module

Everything about a module lives in one entry in `NodeCatalog`: its sockets and
the ops it lowers to. Nothing else in the pipeline needs to change — it shows up
in the palette and compiles on its own. The same entry works from a plugin; see
*Modules from a plugin* above.

```csharp
new NodeDef(
    "pattern.rings", "Rings", "Pattern",
    [..Position(), Num("freq", 4f, 0f, 32f), Num("offset")],
    [Num("out")],
    (em, i) =>
    {
        var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
        return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
    },
    "Concentric sine rings.")
```

Ports typed `Any` pass through whatever arrives, which is how one `Multiply`
works on both a scalar and a colour.

`Position()` is the `x` and `y` pair normalled to Coordinates, and `Domain()` is
a socket normalled to Time; a port declares either by naming a module and one of
its outputs in `NormalledTo`, which a plugin's port may do as readily as one
here. Normal a socket only where the module is useless without that source — see
[ADR-0050](docs/adr/0050-normalled-sockets-carry-a-signal-with-no-wire.md), where
`offset` above is the example of a socket deliberately left alone.

## Files

Patches save as JSON (`.fbk`), and as a **bundle** (`.fbkp`) where the sounds and
pictures they name should travel with them — see [Bundles](#bundles).

**Export…** writes the thing the instrument actually makes, and there is one
button for all of it: the file name decides what you get, and the dialog offers
only the kinds this patch actually reaches — rather than opening one that could
produce a file of nothing. **Length** is the one parameter of a moving export
that cannot be defaulted, since a patch is an endless function of `(x, y, t)`.

A PNG is the odd one out. It ignores the length, being a single frame at the
moment on screen, and it is written at 1920×1080 whatever **Size** says — the
preview's resolution is a matter of keeping up, and a still has nothing to keep
up with.

The video file is an AVI: every frame an independent JPEG, 16-bit PCM interleaved
alongside. Both encoders are written here beside the PNG and WAV ones, so export
needs nothing installed and works headlessly. A patch with nothing wired into the
Output's `left` or `right` gets a video-only file rather than a silent track.

| | |
|---|---|
| Rate | 30 frames a second |
| Size | 1.5 to 2.5 MB a second at 960×540 depending on the patch, and AVI stops at 4 GB — half an hour or so |
| Cost | the picture is rendered on the processor even when the preview is on the GPU, so an expensive patch takes longer to write than to watch |
| Stopping | keeps what was rendered as a shorter video rather than a broken one |

Feedback works in a moving export and does not in a still: one renderer runs the
whole clip, so each frame reads the one before it exactly as on screen. The sound
in it is byte-for-byte the WAV the same patch writes on its own — one oscillator
phase and one delay tail, running the length of the clip.

MJPEG is an old compression and every frame pays full price, which is the cost of
a container simple enough to write by hand under
[ADR-0019](docs/adr/0019-no-third-party-dependencies-in-the-engine.md). Anything
wanting an MP4 can transcode one; this is a file every tool accepts as input.
