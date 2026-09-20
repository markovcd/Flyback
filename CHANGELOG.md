# Changelog

## Unreleased

### Updates
- Flyback updates itself: it checks for a signed release at startup, downloads it in the background, and installs it the next time it starts, then shows what changed since the version it replaced. On by default, in the new Settings → Updates tab.
- Flyback counts how it is used — the version, the operating system, the rough size of the machine, which plugins and sound backend are in use, how many of each kind of module a patch has when it plays, which assistant is asked, how long a run lasts and what it did, and where it crashed. Nothing about you, your machine or your patches. On by default, in the new Settings → Usage tab.

### Plugins
- A plugin built for a Flyback that has since changed what plugins are built against is left out at startup, and About says whether the plugin or Flyback is the one to update.
- A plugin can paint its own module's background: a color of its own, a texture over it, or a picture. On by default, and its animation separately, in the new Settings → Canvas tab.

### Modules
- Added Stroke (a drum envelope counted off the beat), Fade (brings a part in as a level rises), Wander (a slow random value) and Drum (a kick or tom from one envelope) to Voice.
- Added Desk, a four-channel stereo mixer with a trim that chains into bigger ones.
- Added Expression, one block that computes a formula you type over its four inputs, such as `(floor(a * 45) + 0.5) / 45`.
- Arithmetic in the text view, such as `(x * 2 - 1) * aspect`, is one Expression, and an Expression shows there as arithmetic.
- Add, Multiply, Floor and the other one- and two-input Maths modules are Expressions now: the module list finds them by name and adds the Expression.
- Added Trails, which leaves the last frame fading behind the picture.
- Added Hiss (a hi-hat, a snare's wires or a riser from one envelope) and Bell (struck metal from two sines) to Voice.
- Added FM to Voice, a four-operator FM synth for electric pianos, brass and bells, with five algorithms.
- Added Echo to Effects, a stereo delay whose times are counted in steps of the tempo.
- Added Transform (zoom, turn and slide in one), Ink (draws a shape in one color), Vignette (darkens the corners) and Tune (a Quantiser and a Note in one).
- Euclid has a `stroke` output, an envelope on the steps that are hits, and Tempo has `beats`, the count of beats so far.
- Added the Mastering plugin, for the end of a patch: EQ, Width, Crossover (three bands), Compressor, Limiter (nothing past a ceiling), Maximizer (one knob for a louder, denser mix) and Loudness (a LUFS meter).
- The computer keyboard can be laid out by scale, chosen on any MIDI In and saved with the patch: the notes you pick are played one to a key along the A row, with the Q row an octave up and the Z row an octave down.
- Added Text to Picture: lines of text in one of two pixel fonts, drawn as a shape, with a socket that picks the line and one that types it out.

### Presets
- Mycelium, in the Effects plugin: a psybient track of ninety-six bars in six sections, with a picture driven by the same signals. It is the largest preset, and needs the Voice and Picture plugins.
- Bronze, in the Effects plugin: a gamelan in a five-note pelog tuning, on a tempo that slows and quickens, with a mandala struck by the same beats. It needs the Voice and Picture plugins.
- Outrun, in the Effects plugin: a synthwave track on four dark chords, under a drawn scene of a slatted sun, a ridge and a grid that scrolls to the beat. It needs the Voice and Picture plugins.
- Phase, in the Effects plugin: phase music after Steve Reich, two players on one pattern drifting through all twelve canons, with a picture of two dials that shows where they are. It needs the Voice and Picture plugins.
- Fracture, in the Effects plugin: drum and bass at 170 with a synthesized break that gets chopped, rolled and reversed, a Reese bass, and a picture cut into strips along with the drums. It needs the Voice and Picture plugins.
- Dub, in the Effects plugin: dub techno to play rather than listen to. Drums and a sub run on their own, four keys hold a chord over them, and eight panel knobs ride the filter, the echo, the room and the mix, moving the picture with the sound. It needs the Voice and Picture plugins.
- Overworld, in the Effects plugin: a chiptune track on a console's four voices, with a key change and a kick-keyed compressor at the end of the chain, under a side-scroller drawn a pixel at a time. It needs the Voice, Picture and Mastering plugins.
- Captions, in the Picture plugin: lines of text paged and typed out by the clock.
- Acid, Bronze, Mycelium, Nebula, Outrun, Phase, Slow weather and Whole band are rebuilt on the new modules, with up to a fifth fewer modules each.
- Every preset's arithmetic arrives as Expressions, a seventh to a third fewer modules in the big ones, sounding and looking the same.
- Acid is a whole track now: a hundred and twenty-eight bars in two themes, with builds, a breakdown, a bass line and a second 303, and a line that accents and slides.
- Whole band is a whole song now: twelve phrases with verses, a chorus, a bridge and fills, and a snare, plucked strings, a pad and a room added to the band.
- Slow weather is rebuilt around five feedback loops: a drone that bends its own phase, an echo that darkens every time round, two voices that push each other down, and a picture steered by where it was bright a frame ago.
- Settings → Graphics picks which preset Flyback opens on at the next start.
- The preset list is sorted into headed sections: the blank canvas first, then the patches about one idea, then the ones where sound and picture are the same thought, then the showcases.
- The preset button opens a gallery of tiles, each with a picture of what the preset draws (a speaker for one that is only heard), its name and its description.
- Resting the pointer on a preset in the gallery for a second plays it: its picture moves on the tile and its sound fades in, with your patch muted meanwhile.
- The patch on the canvas can be saved as a preset of your own, listed under Your presets at the end of the gallery.
- The preset gallery has a filter box, like the module list.
- The teaching presets are updated, with new ones in the engine and in the Picture, Voice and Mastering plugins.

### Recording and export
- Record counts down on the status bar and takes the patch back to zero, so a take starts where the patch does. Ctrl+R during the count calls it off.
- Settings → Recording sets how long the count-in is, or turns it off, and whether a take rewinds to zero first.
- MP4, WebM, MOV, MP3, M4A and FLAC, encoded by ffmpeg where it is installed. An MP4 is around twenty-five times smaller than the AVI.
- Settings → Recording picks the video and sound formats, and where ffmpeg is if it is not on your `PATH`.
- `flyback-cli render` takes the format from the extension, with `--format` and `--ffmpeg` to override it.
- `flyback-cli render --loudness` says how loud the sound came out, in LUFS, and its true peak.
- A Scope or an Analyzer is drawn in an exported clip.

### Assistant
- The assistant is told what every module does again. Settings → Agent sets how long that briefing may get and names an editable list of modules that are always included. Any module left out is marked on the canvas.
- Settings → Agent has a Probe this model button, which asks the endpoint about the model on the form using the provider, settings and key as they stand there, saved or not. `flyback-cli probe`, which asks about every model, now asks before it starts; `--yes` is for a script.
- The assistant can change the largest presets, and changes what is there instead of rebuilding it.
- The assistant can read the presets, yours included, for ideas.

### Canvas and interface
- Ctrl+Shift+L, or Ctrl+click on the Tidy button, lays out only the selected modules and leaves the rest of the patch where it is.
- A toolbar button swaps the preview and the canvas, for a bigger picture while you patch.
- The module panel's buttons are icons, in a row under the name.
- An Expression's formula is shown in red while it does not read, and says what stopped it.
- Unsaved work survives a crash: the next start offers to restore it.
- A module carries its category's color down its body and a mark of what it does behind its sockets, with a drawing of its own for the ones you would name; groups are drawn to match.
- Ctrl+B switches a module off: what is patched into it comes straight out of it, and nothing does where nothing is patched in.

### Performance
- An output with no wire on it no longer costs anything. The big presets run 2–19% fewer operations a sample, and Random's white is a sixteenth of what it was.

### Fixes
- Opening a patch, from a file, a preset or a recovery, takes the sound back to zero seconds along with the picture.
- A list on a module's panel, like Text's font or Layer's mode, now keeps a pick back to the option it opened on.
- Pressing Escape after "Learn MIDI controller" found no MIDI device no longer crashes.
- A number too large for a number box — in a patch file, or typed into the text and applied — no longer crashes when its module is selected.
- A filter, reverb, delay or loop with silence going into it no longer costs more than one with a signal. A patch with resting parts could crackle because of it.
- Tidy no longer mislays modules on a patch too tall or too wide for the canvas, and lays the big presets out about a third shorter.
- A bundle saved by a newer Flyback, or one using a module you do not have, is refused rather than opened as an empty or partial patch that the next save would write over it.
- Answering "Save…" to the unsaved-changes question and then saving a text copy no longer closes the patch as if it had been saved.
- Fading Volume to zero during a recording no longer stops the file there.
- A module moved with a middle-button pan or a zoom in the middle of the drag stays under the pointer, and the move can be undone.
- In text, an output named straight after a call, as in `tempo(bpm: 104).beats`, is read rather than dropped for the first output.
- In text, anything left on a line after its statement is a complaint rather than skipped.
- Flyback no longer closes while the settings window is open.
- A corrupt sample or picture is reported rather than taking Flyback down with it.
- Sign answers 0 for a value that is not a number instead of stopping the render, and agrees with the GPU about an infinite one.
- `flyback-cli render --size` says a frame is too large rather than failing at the arithmetic.
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
