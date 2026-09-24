# Changelog

## Unreleased

- A patched socket in the panel names the socket at the other end of its wire, `◀ patched from Time.t`, and an output names every socket it feeds; a group's socket in the panel is its module's row, help and slider included.
- Every socket and every setting on a node says what it is for: hovering its row in the panel shows it, the panel lists a module's outputs with theirs, and the assistant and `flyback-cli modules <module>` read the same words.
- The preset site starts with Fractals, a plugin of three modules about one point c: Mandelbrot, which maps every c in the classic colors, Julia, its picture, and Orbit, its sound, from nought or from a pixel of the Julia set, with Dive and Julia walk as presets.
- A note played over a busy MIDI voice gives the voice back to the note it took when let go, so one MIDI In plays legato.
- A patch's groups are written in its text and read back, a group built from text opens shut, and the cursor on a group's block in the text view shows the group in the panel.
- A text patch names the plugins it needs, `requires flyback.picture`, and a build without one says so once.
- A patch's knob panel is written in the text, `panel cutoff = 0.4, cc: 21, device: "…"`, with the sockets that follow each knob and the sums that read one, `t * rate(0..2)`, so a played patch keeps its knobs through the text view and `flyback-cli print`, and a knob turned by hand, moved, renamed, learned, added or removed on the panel is written back into it.
- A patch written out as text follows its own chain, with what the chain reads in the brackets, keeps its arithmetic as arithmetic, functions and panel knobs included, and names a sum after what it drives.
- In the text language a pipe lands on `in`, a module's only socket, a position, a module's one color socket for a color, or the socket written `socket: _`, and a pipeline inside a call's argument is refused.
- `flyback-cli check --json` and the assistant's `write_patch` give each complaint about a text patch a stable code.
- A `//` comment in a text patch is one complaint that points at `#` and at groups, and the assistant is told to write both.
- The text language refuses a name bound twice, a `let` of `t`, `x` or `out`, a socket wired twice, a knob set twice, a group, `panel` or `requires` inside a group and a group of one module, and `flyback-cli check --json` reports it by line.
- A rewire in a big patch goes back to compiled speed about three times sooner.
- An opened preset or file starts its sound and picture together once both are compiled, from the beginning, and the status line says "Compiling…" while it waits.
- A large patch no longer freezes the window while its shader is built.
- AVI clips are encoded three times faster, so a full-HD AVI take keeps up at 30 fps.
- `flyback-cli render` takes the size, frame rate, quality, format and ffmpeg it is not given from the editor's settings.
- `flyback-cli print --preset <name>` writes a shipped preset out as text.
- `flyback-cli modules <module>` describes one module: its sockets with their defaults and ranges, where a pipe lands, and what it does.
- Added Crush, a bitcrusher.
- Added Arc, part of a ring opening from the top, filled like a dial by its `sweep`, and Four forms has one filling in its middle.
- Added Chance: each note on a gate plays on its `gate` as often as a knob says, and on its `else` the rest of the time, and the Heads or tails preset plays one riff through it.
- `flyback-cli compare` plays two patches side by side and says whether they are the same instrument, bit for bit, or where they part.
- Added Clock In, which keeps a patch's beats, tempo and transport to the MIDI clock of a drum machine or sequencer.
- A MIDI In can listen to one channel of its instrument.
- A knob's menu learns it and every knob after it in one pass, and a knob learned from an instrument with a track per channel keeps the channel.
- The module list offers a plugged-in instrument Flyback knows by name, under Instruments, and adds it whole: its clock and a MIDI In per track.
- Flyback knows the Elektron Syntakt by name: a MIDI In offers its tracks, a knob binds to one of its knobs from the panel's menu, and a learned knob says what it follows in the box's own words. Other instruments are a `.json` file in the data folder's `instruments`.
- Settings → Agent is now Settings → Assistant.
- The assistant's conversation shows the briefing it is handed and the handbook text it looks up, each hidden by its own box in Settings → Assistant.
- Patches carry an author and tags, edited in the panel and written as `author "..."` and `tags "..."` in the text, and found by in the preset gallery.
- A patch has a description, edited by double-clicking it in the panel with nothing selected, written as `description "..."` in the text and shown in the preset gallery.
- Saving a preset under a name already saved asks first, in place.
- Settings → Graphics sends the full-screen picture to another monitor, or to a chosen one, and leaves the editor where it is.
- A patch's knobs can be played over the full-screen picture, in the editor and the viewer.
- The viewer opens a patch with no picture as just its buttons and knobs.
- Dragging the right button on an unpatched input turns its value.
- Frequency and delay-time knobs sweep in decades, and an oscillator's `freq` reaches from a slow wobble up to 20 kHz.
- A panel knob can sweep its sockets logarithmically, from its menu.
- Added Auto remap, a Remap whose ranges are read off its wires and set as fractions of each end.
- A wire between two different ranges has a mark that puts an Auto remap into it.
- A wire swinging past the range its socket takes is drawn in orange and warned about.
- The Frequency module is gone: an oscillator's own `freq` knob reaches audible pitches, and a patch that still holds one no longer opens.
- Random is now Noise and the old Noise is now Clouds, in the canvas and the text, and a patch holding either old one no longer opens.
- A turning knob holds the pointer still, so the edge of the screen never stops it.
- A dragged side panel comes back at the width it was left at.
- Double-clicking a box looks inside it, over the rest of the patch, without opening it.
- A patch or bundle from somebody else reaches only its own files.
- The preset gallery's tiles and auditions run as compiled code.
- `flyback-cli render` and the assistant's looking and listening run the patch as compiled code, and `flyback-cli render --interpreted` keeps it on the interpreter.
- A large patch's sound runs more than twice as fast.
- The preset gallery draws only the tiles in sight, and keeps their pictures between runs.
- A module read through a Probe or Overtones is copied only as far as it depends on the place being read.
- Presets people make can be shared on a site of their own, with a picture and a sound of each, linked from the website.
- Opening a signed `.fbkp` plugin package shows what the plugin is, with its preview, author, description and tags, what it adds and reaches and, when asked, installs or updates its build for this system and restarts Flyback; `flyback-cli pack-plugin` makes one, signed with a key from `flyback-cli plugin-key`.
- Plugin packages can be shared on the preset site, with their preview and tags, listed once the admin has published them.
- The preset gallery lists the presets shared on the preset site, searched with its filter, and opens one when picked.
- `flyback-viewer` and `flyback-cli` list the plugins they loaded on the terminal, as the editor does.
- Patches, bundles, text files and plugin packages each have an icon of their own.
- F11 switches the viewer to full screen and back, and `--full-screen` refuses a patch with no picture.
- A plugins window, beside the settings button, takes over About's plugin list: it searches the installed plugins and the shared ones together, says what failed to load, and a plugin clicked there installs, updates or removes.
- On macOS, a file opened from Finder or dropped on the Dock icon opens in Flyback.
- The shipped plugins have a name, author, description and tags, and the module plugins a preview of their modules.
- A plugin declares its modules, so the install dialog and the shared plugins site list them by name, and one that registers a module it did not declare is refused.
- Shared presets and plugins can be reported to the preset site's admin, from the site, the preset gallery and the plugins window, and the admin lists the reports.
- Shared presets and plugins are rated with stars on the preset site, and the preset gallery and the plugins window show them.
- A patch that will not open for want of a plugin offers the one the plugin site has, and opens the plugins window at it.
- Added Irrational: seven concentric orbits a turn apart every four minutes, each on a knob, drawing together into an alignment that is never quite exact and striking a degree of A minor into a long delay and a large room.
- Installing a plugin a patch was refused for opens that patch again once Flyback has restarted.
- The preset site starts with Tranquility: psytrance in G sharp with an FM lead that talks, on nine panel knobs, a rolling FM bass ducked under the kick, three builds that each land on a drop and Apollo 11 on the radio, subtitled, down a tunnel with a moon at the end.
- The preset site starts with Figures, a plugin of three modules that are each a picture and a sound: Plate, a struck plate and the sand figure it settles into; Harmonograph, damped pendulums whose drawing is their chord; and Overtones, any picture read along a row as the overtones of a tone. Vigil, its preset, is dark ambient in D phrygian dominant played on all three.
- The envelope at the end of the status bar writes to Flyback's author, with what build it was written from.
- Noise read off a fast clock no longer sticks at full level after a day or more of playing.
- A picture drawn on the graphics card no longer stutters after hours or days of playing.
- Fracture lights the same squares on the graphics card as on the processor.
- Renaming a module or a group in the panel puts the caret beside the name rather than at the panel's left edge.

## 0.4.0 — 2026-09-21

256 commits since 0.3.0.

### Updates
- Flyback updates itself: it checks for a signed release at startup, installs it the next time it starts and shows what changed. On by default, in the new Settings → Updates tab.
- Settings → Files picks what opens a .fbk, .fbkb or .fbks file: nothing, the editor or the viewer.
- Flyback counts how it is used, and nothing about you, your machine or your patches. On by default, in the new Settings → Usage tab.

### Plugins
- A plugin built for a Flyback that has since moved on is left out at startup, and About says whether the plugin or Flyback is the one to update.
- A plugin can paint its own module's background, in the new Settings → Canvas tab.
- Settings → Sound and Settings → MIDI name the plugin playing the sound and hearing the keyboard.

### Modules
- Added Stroke, Fade, Wander, Drum, Hiss, Bell and FM to Voice.
- Added Desk (a four-channel mixer), Duck (a sidechain) and Echo (a delay counted in steps of the tempo).
- Added Send and Receive, which carry a signal across the patch on a named bus.
- Added Expression, one block that computes a formula you type over its four inputs. Arithmetic in the text view is one, and Add, Multiply, Floor and the other one- and two-input Maths modules are Expressions now.
- Added Trails, Blur, Transform, Ink and Vignette for the picture, and Tune, a Quantiser and a Note in one.
- Added Text to Picture: lines of text in one of two pixel fonts, typed out by a signal.
- Euclid has a `stroke` output, and Tempo has `beats`.
- Added the Mastering plugin: EQ, Width, Crossover, Compressor, Limiter, Maximizer and a LUFS meter.
- The computer keyboard can be laid out by scale, chosen on any MIDI In, and Settings → MIDI picks what the first MIDI In gets.
- Filter, Random, Slew, Drive, Delay and Reverb are built into Flyback, so a patch can use them with no plugin installed.
- Mixer, Desk, Send and Receive have a section of their own, Routing.

### Presets
- Mycelium: a psybient track of ninety-six bars, and the largest preset.
- Bronze: a gamelan in a five-note pelog tuning, with a mandala struck by the same beats.
- Outrun: a synthwave track under a drawn scene of a sun, a ridge and a grid.
- Phase: phase music after Steve Reich, with a picture of two dials.
- Fracture: drum and bass at 170 with a synthesized break, chopped and reversed.
- Dub: dub techno to play, with four keys of chord and eight panel knobs.
- Overworld: a chiptune track under a side-scroller drawn a pixel at a time.
- Captions, Dodge (a game played on the keys Z to M) and Duck join the Picture and Effects plugins.
- Acid is a whole track of a hundred and twenty-eight bars, Whole band is a whole song, and Slow weather is rebuilt around five feedback loops.
- The other presets are rebuilt on the new modules, a seventh to a third fewer modules in the big ones, sounding and looking the same, and the teaching presets are updated.
- The preset button opens a gallery of tiles that filters as you type and plays a preset when the pointer rests on it, sorted under headings, with your own presets saved at the end.
- Settings → Graphics picks which preset Flyback opens on.

### Recording and export
- Recording counts in and starts at zero, and exports MP4, WebM, MOV, MP3, M4A and FLAC through ffmpeg; `flyback-cli render` takes the format from the extension, and `--loudness` reports LUFS.
- A Scope or an Analyzer is drawn in an exported clip.

### Viewer
- A third program, `flyback-viewer`, opens a patch and plays it: picture and sound, no editor, nothing written.

### Assistant
- The assistant is told what every module does again, within a budget Settings → Agent sets.
- Settings → Agent has a Probe this model button.
- The assistant can change the largest presets and read the presets for ideas.
- A long block in the transcript arrives folded, every frame the assistant renders is shown under its caption, and the panel is a column beside the patch.

### Canvas and interface
- Ctrl+scroll, Ctrl+plus and Ctrl+minus change the text view's font size, and Ctrl+0 resets it.
- A module wears its category's color and a mark of its own, drawn the same on the canvas, in the module list and in the panel.
- Ctrl+Shift+L, or Ctrl+click on Tidy, lays out only the selected modules.
- A toolbar button swaps the preview and the canvas.
- Ctrl+P pauses and plays the patch, and the full-screen preview has the viewer's controls.
- An Expression's formula is red while it does not read, and says what stopped it.
- Unsaved work survives a crash and is restored at the next start.
- Ctrl+B switches a module or a box off, and holding the right button does it until you let go.
- About has a bitcoin address to donate to, as a QR code.
- Flyback reopens as you left it: window, panels, text view and swapped preview.
- Ctrl+O and Ctrl+S open and save, and Ctrl+D duplicates the selection.
- Esc backs out of a drag on the canvas.
- The status bar shows the time as minutes and seconds, as in 1:05.25.

### Performance
- An output with no wire on it no longer costs anything, and the big presets run 2–19% fewer operations a sample.

### Fixes
- Opening a patch takes the sound back to zero along with the picture.
- A list on a module's panel keeps a pick back to the option it opened on.
- A filter, reverb, delay or loop with silence going into it no longer costs more than one with a signal, which could make a patch with resting parts crackle.
- Tidy no longer mislays modules on a patch too tall or too wide for the canvas.
- A bundle saved by a newer Flyback, or using a module you do not have, is refused rather than opened as a partial patch that the next save would write over it.
- Answering "Save…" and then saving a text copy no longer closes the patch as if it had been saved.
- Fading Volume to zero during a recording no longer stops the file there.
- A module moved with a middle-button pan or a zoom in the middle of the drag stays under the pointer, and the move can be undone.
- In text, `tempo(bpm: 104).beats` is read rather than dropped for the first output, anything left on a line after its statement is a complaint, and `%` takes the remainder Modulo does.
- Flyback no longer closes while the settings window is open.
- A corrupt sample or picture, a number too large for a number box, Escape after "Learn MIDI controller" found no device, and a frame too large for `flyback-cli render --size` are reported rather than crashing.
- Sign answers 0 for a value that is not a number, and agrees with the GPU about an infinite one.
- The macOS bundle carries the real version.
- A Flyback built from a source archive or a plain `docker build` no longer updates itself or counts its use.
- Several other bugfixes.

## 0.3.0 — 2026-09-17

71 commits since 0.2.0.

### Feedback loops
- Loops now draw as well as sound. A loop carries each pixel's value from the previous frame, on both the CPU renderer and the GPU shader.
- Removed the Unit Delay module. The wire that closes a loop is the delay, and the canvas draws it dashed.
- A module can be wired to itself, and the wire is drawn beneath the module.
- The wire a loop is cut at no longer changes when a module is dragged. Wires whose input sits left of their output are drawn as a U-turn.

### Knobs and MIDI
- A patch carries a panel of knobs, shown under the canvas with Ctrl+K. Knobs can be added, renamed and removed, and reordered by dragging their names or from their menu.
- Any unwired socket can follow a knob over its own range: click the knob's name, then socket rows on the canvas. The inspector shows a linked socket's knob and range.
- Turning a knob on screen or on a bound MIDI controller changes the playing patch without a recompile. A knob learns its controller from its menu, and the MIDI settings tab chooses whether a controller jumps or picks up.
- The panel wraps knobs onto more rows and is resized by dragging the splitter above it.

### Modules
- Added Random (white and pink noise, stepped or drifting values), Slew, Decay and Euclid to Voice.
- Added String, a Karplus-Strong plucked-string voice.
- Added Layer (eight blend modes through a mask) and Line (distance to a segment) to Picture.
- Added Analyzer, which charts the spectrum of the audio output on a log frequency axis.
- Added the Euclid kit preset. Played is rebuilt as four plucked strings into a reverb and moves to the Effects plugin.
- The Output loses its scan knobs, and the speakers are always evaluated at the origin. The picture is heard through a Scan instead, and Coordinates gains an `aspect` output.
- The Output's gain is renamed Volume. Turning it to zero closes the audio device, which replaces the Audio on/off toggle.
- Color inputs, and the Output's left and right, lose a slider that could only offer a grey or a hum, and take a wire only.

### Performance
- The CPU runs a patch as compiled IL once it has been built, and interprets it until then with no audible or visible hand-over. Frames run 1.5–2.1x faster and audio callbacks about 1.7x. A knob move rebinds without recompiling, and `--interpreted` keeps a run on the interpreter.

### Settings
- The settings window has a tab per section (Graphics, Recording, Sound, MIDI and Agent) with Save and Cancel. Choices are kept in `output.json` beside `assistant.json` and applied at startup.
- Graphics: output size, now with 1440p, 4K, 4:3, square, portrait and ultrawide; GPU or CPU rendering; and an optional cap on the preview's frame rate. The live sound's aspect follows the chosen size.
- Recording: a take's frame rate and JPEG quality.
- Sound: latency and the output device, applied on Save while the sound carries on. WASAPI, CoreAudio and ALSA list their devices, and System default follows the system's default device when it changes.
- Agent: how many turns a conversation may have.

### Recording and playback
- Record and Rewind move from the Output's panel to the toolbar, with glyphs, and Ctrl+R starts or stops a take.
- Removed the Output panel's Export button. `flyback-cli render` writes the same files.
- Rewind clears the playing program's memory on the audio thread, so it no longer sets off a loud, clipped burst.

### Assistant
- Conversations are saved with their patch: inside a bundle, or alongside a `.fbk`/`.fbks` in the user's data folder.
- Added a New conversation button that starts over on the same patch.
- None can be picked as a provider, and a saved provider that fails to load falls back to None.

### Opening files
- Open a patch by dropping it on the window or passing it on the command line.
- macOS opens `.fbk`, `.fbkb` and `.fbks` from Finder. Linux gets a `.desktop` file and MIME package for "Open With".

### Command line
- Added `print`, which writes any patch as text, with `--check` to confirm the printing compiles to the same program.
- Added `modules`, which lists the installed modules by provider, with `--json`.
- `check --strict` fails on warnings, `pack` supports `--json`, and the CLI supports shell completion.

### Canvas and interface
- The middle button pans while a wire or other drag is in progress, and the drag carries on afterwards.
- A dialog raised while the window is in the background flashes the taskbar, dock or window-list entry. Every dialog casts a shadow.
- The status bar shows the preview's frames per second and which renderer is actually drawing.
- The layout places a group as a single block, and a tidied patch is centered on the canvas.
- The canvas is 15000×10000, and zoom goes out to an eighth.
- Ctrl+E opens every group in the selection, and Ctrl+Shift+E closes them.
- Plugin information moves from the status bar into About. The status bar shows the report on the left and wires and time on the right.
- A printing of a patch with no bindings starts on its first statement rather than a blank line.

## 0.2.0 — 2026-09-10

76 commits since 0.1.0.

### Text language and live coding
- Added a text language that parses to a patch: lexer, parser, binder and step notation, specified in `docs/language.md`.
- Added a printer that writes any patch back out as text, with line breaks laid out automatically.
- Added the `.fbks` source format, and patches can be saved as it. Bundles are now `.fbkb`.
- Added a code view built on AvaloniaEdit, with line numbers, error highlighting, current-line marking and syntax coloring.
- The opened file decides who owns the patch. With a `.fbks` the text is the document and Ctrl+Enter builds it onto a locked canvas. With a `.fbk`, bundle or preset the graph owns it and the text view shows a read-only printing.
- Added "Edit on the canvas" to hand ownership back from the text to the graph without saving a file.
- Modules keep their names and their state across a rebuild, so editing a playing patch no longer restarts every voice or empties every delay line.
- The caret selects a module in the inspector. Knob changes, sequencer tunes, scales, files and plugin fields are written back into the text where it already says them, and number boxes update the text as you type or scroll.
- Plugin fields are now part of the language, so printing a patch no longer loses them.
- Picking a preset while the text view is showing opens it as text, and loading a document no longer switches views.

### Undo
- One undo covers one action, whether that was a drag, a word typed or a document arriving, across both the text and the canvas.
- Ctrl+Z waits for a drag on the canvas to finish. Redo after an undo that crosses between text and canvas uses whichever stack still has the step.

### Assistant
- The assistant now builds patches by writing the text language rather than through graph calls.
- Added a Gemini adapter. Gemini models hear the patch's sound themselves instead of relying on a second model to describe it.
- Providers declare their own settings (text, list or switch), and the shell only draws them.
- Model lists come from probing the endpoint rather than a hardcoded list, which also replaces a default model that no longer accepted new keys.
- Added an optional setting that logs each conversation to its own file.
- Closing Settings any way other than Save restores the previous values.
- Tools are now defined as single entries of name, description, handler and availability.

### Performance
- Video rendering splits the program into per-frame, per-row and per-pixel stages, so work that can't vary between pixels runs once per frame or row. Renderer speedups range from 1.22x to 4.61x on the bundled presets, and audio is 3–8% faster.

### Modules
- Scope: the window is no longer capped at two seconds.
- Sequencer: the gate length now stops at half a step whether set by knob or wire.
- RGB starts out white instead of black.
- Knobs that count things now snap to whole numbers.
- Scan no longer asks for a wire into its clock socket.
- Fixed the descriptions of MIDI In's voice field and the Probe's LFO window.

### Interface
- The inspector gives a knob's reading its own wider column to the left of its number box, with bar widths matched across a module.
- The preview box hides when it could only show black, and gives its space to the inspector.
- The assistant footer no longer repeats a stale notice once a key is present.
- The computer keyboard stops acting as an instrument while the code editor has focus.
- Opening or saving a patch clears the preset list's selection.
- Added a glyph button in front of the preset picker, and moved the program group next to the patch controls.
- Added shared typography steps next to colors, and a single grey for secondary text.

### Presets
- Shipped presets no longer store module coordinates. They're placed by the automatic layout.

### Platform and internals
- Merged the sound and MIDI plugins into one plugin per platform: `WinIO`, `MacIO` and `LinuxIO`.
- Split the node editor into one class across a file per region.
- Unified the five routes by which a patch document arrives: its name, file kind and asset lookup now travel together.
- Renamed `PatchIo` to `PatchIO`, and added tests for the CLI patches, live values, choice fields and the image library.
